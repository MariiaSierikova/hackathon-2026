namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;
    using System.Globalization;

    // Pomodoro timer shown on the MX Keypad while it is active.
    //   focus (default 25 min) -> short break (5) ... after `Rounds` focus sessions a long break (15)
    //   with camera presence on: the FOCUS clock only runs while you are at the desk, and an absence of
    //   `ResetAfterAwayMin` minutes or more resets the timer ("you already took a break").
    // Breaks always run on the wall clock (you are expected to leave the desk).
    // The end of a focus session publishes the trigger `break_due`, the end of a break `break_over`
    // (both go through the notification policy like every other trigger).
    public static class PomodoroState
    {
        public enum Phase { Work, Short, Long }

        private static readonly Object Gate = new();

        // ---- settings (set from the settings page, see UiSettings) ----
        public static Int32 WorkMin { get; private set; } = 25;
        public static Int32 ShortMin { get; private set; } = 5;
        public static Int32 LongMin { get; private set; } = 15;
        public static Int32 Rounds { get; private set; } = 4;
        public static Boolean AutoNext { get; private set; }
        public static Boolean UsePresence { get; private set; } = true;
        public static Int32 ResetAfterAwayMin { get; private set; } = 5;

        // ---- state ----
        public static Boolean Active { get; private set; }     // the timer view is wanted (feature on)
        public static Boolean Running { get; private set; }
        public static Boolean AtDesk { get; private set; } = true;
        public static Phase Current { get; private set; } = Phase.Work;
        public static Int32 Round { get; private set; } = 1;
        public static Int32 LeftSec { get; private set; } = 25 * 60;

        private static DateTime _awaySince = DateTime.MinValue;
        private static Boolean _resetWhileAway;

        // Replaceable clock (tests).
        public static Func<DateTime> Now = () => DateTime.UtcNow;

        public static event Action Changed;

        public static Int32 TotalSec => (Current == Phase.Work ? WorkMin : Current == Phase.Short ? ShortMin : LongMin) * 60;

        // true while the clock actually counts down
        public static Boolean Counting => Active && Running && (Current != Phase.Work || !UsePresence || AtDesk);

        public static void Configure(Int32 work, Int32 shortBreak, Int32 longBreak, Int32 rounds, Boolean autoNext, Boolean usePresence, Int32 resetAfterAwayMin)
        {
            lock (Gate)
            {
                var oldTotal = TotalSec;
                WorkMin = Math.Clamp(work, 1, 180);
                ShortMin = Math.Clamp(shortBreak, 1, 60);
                LongMin = Math.Clamp(longBreak, 1, 120);
                Rounds = Math.Clamp(rounds, 1, 12);
                AutoNext = autoNext;
                UsePresence = usePresence;
                ResetAfterAwayMin = Math.Clamp(resetAfterAwayMin, 1, 120);

                // an untouched clock follows the new length; a running one keeps its remaining time
                if (!Running && LeftSec == oldTotal)
                {
                    LeftSec = TotalSec;
                }
                else if (LeftSec > TotalSec)
                {
                    LeftSec = TotalSec;
                }
            }

            Raise();
        }

        // ---- controls ----
        public static void Start()
        {
            lock (Gate)
            {
                Active = true;
                Running = true;
                if (LeftSec <= 0)
                {
                    LeftSec = TotalSec;
                }
            }

            Raise();
        }

        public static void Stop()
        {
            lock (Gate)
            {
                Active = false;
                Running = false;
            }

            Raise();
        }

        public static void TogglePause()
        {
            lock (Gate)
            {
                if (!Active)
                {
                    Active = true;
                    Running = true;
                }
                else
                {
                    Running = !Running;
                }
            }

            Raise();
        }

        public static void Reset()
        {
            lock (Gate)
            {
                ResetLocked(keepRunning: false);
            }

            Raise();
        }

        private static void ResetLocked(Boolean keepRunning)
        {
            Current = Phase.Work;
            Round = 1;
            LeftSec = WorkMin * 60;
            Running = keepRunning && Running;
        }

        // Skip to the next phase without firing a trigger.
        public static void Skip()
        {
            lock (Gate)
            {
                NextPhaseLocked(fire: false);
            }

            Raise();
        }

        // Camera presence: false = nobody in front of the screen.
        public static void SetPresence(Boolean atDesk)
        {
            lock (Gate)
            {
                if (atDesk == AtDesk)
                {
                    return;
                }

                AtDesk = atDesk;
                if (!atDesk)
                {
                    _awaySince = Now();
                    _resetWhileAway = false;
                }
            }

            Raise();
        }

        // Demo helper: pretend the user has been away for `ResetAfterAwayMin` minutes already.
        public static void DemoSkipAway()
        {
            lock (Gate)
            {
                if (!AtDesk)
                {
                    _awaySince = Now() - TimeSpan.FromMinutes(ResetAfterAwayMin) - TimeSpan.FromSeconds(1);
                }
            }

            Tick(0);
        }

        // Called once a second by the plugin (seconds = elapsed time; tests can pass more).
        public static void Tick(Int32 seconds = 1)
        {
            String fire = null;
            lock (Gate)
            {
                if (!Active)
                {
                    return;
                }

                // away long enough: the session starts over
                if (UsePresence && !AtDesk && Current == Phase.Work && !_resetWhileAway
                    && Now() - _awaySince >= TimeSpan.FromMinutes(ResetAfterAwayMin))
                {
                    _resetWhileAway = true;
                    ResetLocked(keepRunning: true);
                    PluginLog.Info($"Pomodoro: away {ResetAfterAwayMin} min or more, timer reset");
                }

                if (Counting && seconds > 0)
                {
                    LeftSec -= seconds;
                    if (LeftSec <= 0)
                    {
                        fire = NextPhaseLocked(fire: true);
                    }
                }
            }

            if (fire != null)
            {
                TriggerBus.Publish(new TriggerMessage(fire, 1.0));
            }

            Raise();
        }

        // Moves to the next phase. Returns the trigger to publish (focus ended -> break_due, break ended -> break_over).
        private static String NextPhaseLocked(Boolean fire)
        {
            String trigger = null;
            if (Current == Phase.Work)
            {
                trigger = fire ? "break_due" : null;
                if (Round >= Rounds)
                {
                    Current = Phase.Long;
                    Round = 1;
                }
                else
                {
                    Current = Phase.Short;
                }
            }
            else
            {
                trigger = fire ? "break_over" : null;
                if (Current == Phase.Short)
                {
                    Round++;
                }

                Current = Phase.Work;
            }

            LeftSec = TotalSec;
            Running = AutoNext && fire;
            if (!fire)
            {
                Running = Active && Running;
            }

            return trigger;
        }

        private static void Raise() => Changed?.Invoke();

        public static String PhaseName => Current switch { Phase.Work => "work", Phase.Short => "short", _ => "long" };

        public static String ToJson() => String.Create(CultureInfo.InvariantCulture,
            $"{{\"active\":{B(Active)},\"running\":{B(Running)},\"counting\":{B(Counting)},\"atDesk\":{B(AtDesk)},\"phase\":\"{PhaseName}\",\"round\":{Round},\"rounds\":{Rounds},\"left\":{Math.Max(0, LeftSec)},\"total\":{TotalSec}}}");

        private static String B(Boolean v) => v ? "true" : "false";
    }
}

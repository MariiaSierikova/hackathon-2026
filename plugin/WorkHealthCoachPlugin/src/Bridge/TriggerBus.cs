namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;

    // Everything that reacts to a trigger (haptics, Keypad widgets, overlay) subscribes here.
    public record TriggerMessage(String Trigger, Double Severity, Boolean Force = false, String Only = null);   // Force = development only: skip the notification policy. Only = with Force, deliver just one channel (haptic | keypad | screen | audio)

    public static class TriggerBus
    {
        public static event Action<TriggerMessage> Received;

        public static void Publish(TriggerMessage message)
        {
            try
            {
                Received?.Invoke(message);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"TriggerBus subscriber failed for '{message.Trigger}'");
            }
        }
    }
}

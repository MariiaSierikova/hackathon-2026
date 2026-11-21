"""Small always-visible Pomodoro status chip (top-right). Click-through, always on top, hidden from screen sharing."""
from PySide6 import QtCore, QtWidgets

from overlay import exclude_from_capture

COLORS = {"work": "#2e7d32", "paused": "#6b7280", "due": "#c62828"}


class Chip(QtWidgets.QLabel):
    def __init__(self, get_status):
        super().__init__()
        self.get_status, self.excluded = get_status, False
        self.setWindowFlags(QtCore.Qt.FramelessWindowHint | QtCore.Qt.WindowStaysOnTopHint | QtCore.Qt.Tool
                            | QtCore.Qt.WindowTransparentForInput | QtCore.Qt.NoDropShadowWindowHint)
        self.setAttribute(QtCore.Qt.WA_ShowWithoutActivating)
        self.setAttribute(QtCore.Qt.WA_TransparentForMouseEvents)
        self.setAlignment(QtCore.Qt.AlignCenter)
        self.timer = QtCore.QTimer(self, interval=500, timeout=self.refresh)
        self.timer.start()

    def refresh(self):
        state, remaining = self.get_status()
        if state is None:
            self.hide()
            return
        m, s = divmod(int(remaining), 60)
        text = {"work": f"WORK  {m:02d}:{s:02d}", "paused": f"PAUSED  {m:02d}:{s:02d}", "due": "TAKE A BREAK"}[state]
        self.setText(text)
        self.setStyleSheet(f"background:{COLORS[state]}; color:white; font:bold 16px Consolas,Menlo,monospace;"
                           "border-radius:10px; padding:6px 14px;")
        self.adjustSize()
        g = QtWidgets.QApplication.primaryScreen().availableGeometry()
        self.move(g.right() - self.width() - 16, g.top() + 16)
        if not self.isVisible():
            self.show()
        if not self.excluded:
            exclude_from_capture(self)
            self.excluded = True

"""Second-face logic for "person behind you": who is the user, who are the others, and a stable id per person.

Pure logic (no camera, no OpenCV): boxes in -> samples out. Samples are what the plugin's POST /behind wants:
  x    = horizontal centre of the face, 0 (left) .. 1 (right) of the picture
  size = face width / picture width   (bigger = closer)
"""
from typing import List, NamedTuple, Optional, Tuple


class Face(NamedTuple):
    x: float
    size: float


def to_faces(boxes, frame_w):
    """OpenCV boxes (x, y, w, h) in pixels -> Face list."""
    return [Face((x + w / 2.0) / frame_w, w / float(frame_w)) for (x, y, w, h) in boxes]


def split_user_and_others(faces: List[Face]) -> Tuple[Optional[Face], List[Face]]:
    """The user is the biggest (closest) face; every other face is a possible person behind / beside you."""
    if not faces:
        return None, []
    ordered = sorted(faces, key=lambda f: f.size, reverse=True)
    return ordered[0], ordered[1:]


class IdTracker:
    """Gives each other-person a stable integer id between frames (nearest neighbour on position and size), so the plugin can follow
    them over a few seconds and tell 'walks past' from 'comes to my desk'."""

    def __init__(self, max_dx=0.18, ttl_s=2.5):
        self.max_dx, self.ttl_s = max_dx, ttl_s
        self._tracks = {}          # id -> (Face, last_seen)
        self._next = 2             # 1 is the user

    def assign(self, faces: List[Face], now: float):
        """Returns [(id, Face)] for this frame."""
        for tid in [t for t, (_, seen) in self._tracks.items() if now - seen > self.ttl_s]:
            del self._tracks[tid]
        pairs = sorted(((abs(f.x - tf.x) + 2 * abs(f.size - tf.size), fi, tid)
                        for fi, f in enumerate(faces) for tid, (tf, _) in self._tracks.items()
                        if abs(f.x - tf.x) <= self.max_dx), key=lambda p: p[0])
        used_f, used_t, out = set(), set(), []
        for _, fi, tid in pairs:
            if fi in used_f or tid in used_t:
                continue
            used_f.add(fi)
            used_t.add(tid)
            out.append((tid, faces[fi]))
            self._tracks[tid] = (faces[fi], now)
        for fi, f in enumerate(faces):
            if fi not in used_f:
                tid = self._next
                self._next += 1
                out.append((tid, f))
                self._tracks[tid] = (f, now)
        return out

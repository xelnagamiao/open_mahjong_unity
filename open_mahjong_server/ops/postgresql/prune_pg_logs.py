"""Keep seven Shanghai calendar days of PostgreSQL collector text logs. Preview unless --apply."""
import argparse
import datetime as dt
import errno
import json
import os
import re
import signal
import stat
import sys
from contextlib import contextmanager
from pathlib import Path


KEEP_DAYS = 7
SHANGHAI = dt.timezone(dt.timedelta(hours=8))
DAILY = re.compile(r"postgresql-([0-9]{4})-([0-9]{2})-([0-9]{2})\.log\Z")


def _day(name):
    match = DAILY.fullmatch(name)
    if match:
        try:
            return dt.date(*(int(part) for part in match.groups()))
        except ValueError:
            pass
    return None


def _directory(path):
    path = Path(path)
    if (not path.is_absolute() or path == path.parent or path.resolve() != path
            or not path.is_dir()):
        raise ValueError("expected a real, absolute, non-root directory: " + str(path))
    return path


def _active(data_dir, log_dir):
    marker = data_dir / "current_logfiles"
    if not stat.S_ISREG(marker.lstat().st_mode):
        raise ValueError("invalid PostgreSQL current_logfiles")
    active = set()
    has_daily_stderr = False
    for line in marker.read_text(encoding="utf-8").splitlines():
        fields = line.split(None, 1)
        if len(fields) != 2 or fields[0] not in ("stderr", "csvlog", "jsonlog"):
            raise ValueError("unrecognized PostgreSQL current_logfiles entry")
        target = data_dir / fields[1]  # absolute paths replace data_dir
        path = Path(os.path.abspath(str(target)))
        if (target.resolve() != path or not stat.S_ISREG(path.lstat().st_mode)):
            raise ValueError("invalid active PostgreSQL log: " + str(path))
        active.add(path)
        if fields[0] == "stderr" and path.parent == log_dir and _day(path.name):
            has_daily_stderr = True
    if not has_daily_stderr:
        raise ValueError("log directory is not the cluster's daily stderr log directory")
    return active


def _identity(s):
    return s.st_dev, s.st_ino, s.st_nlink, s.st_size, s.st_mtime_ns


@contextmanager
def _exclusive_log(path, expected_stat):
    # A write lease conflicts even with existing readers that do not take advisory locks.
    # No /proc inspection or ptrace privileges. Without lease support, never unlink.
    import fcntl
    if not hasattr(fcntl, "F_SETLEASE"):
        raise RuntimeError("kernel file leases are required")
    try:
        fd = os.open(str(path), os.O_RDWR | os.O_NOFOLLOW | os.O_NONBLOCK)
    except FileNotFoundError:
        yield False
        return
    try:
        if _identity(os.fstat(fd)) != _identity(expected_stat):
            yield False
            return
        old_handler = signal.signal(signal.SIGIO, signal.SIG_IGN)
        try:
            try:
                fcntl.fcntl(fd, fcntl.F_SETLEASE, fcntl.F_WRLCK)
            except OSError as exc:
                if exc.errno != errno.EAGAIN:
                    raise
                yield False
                return
            try:
                yield fcntl.fcntl(fd, fcntl.F_GETLEASE) == fcntl.F_WRLCK
            finally:
                fcntl.fcntl(fd, fcntl.F_SETLEASE, fcntl.F_UNLCK)
        finally:
            signal.signal(signal.SIGIO, old_handler)
    finally:
        os.close(fd)


def prune(log_dir, data_dir, *, today, apply=False):
    log_dir, data_dir = _directory(log_dir), _directory(data_dir)
    if (log_dir == data_dir or log_dir in data_dir.parents or
            (data_dir in log_dir.parents and log_dir not in (data_dir / "log", data_dir / "pg_log"))):
        raise ValueError("refusing a cluster data directory as the log directory")
    active = _active(data_dir, log_dir)
    first_kept = today - dt.timedelta(days=KEEP_DAYS - 1)
    report = dict(first_kept=first_kept.isoformat(), apply=apply,
                  eligible=[], deleted=[], skipped={})
    # A log rotated at midnight may have its last write just after the date in its name.
    # Trust the filename for retention, but conservatively retain a file still written today.
    for path in sorted(log_dir.iterdir()):
        day = _day(path.name)
        if day is None or day >= first_kept:
            continue
        try:
            original = path.lstat()
            if not stat.S_ISREG(original.st_mode) or original.st_nlink != 1:
                reason = "not a single-link regular file"
            elif path in active:
                reason = "active"
            elif dt.datetime.fromtimestamp(original.st_mtime, SHANGHAI).date() >= today:
                reason = "recent write"
            else:
                with _exclusive_log(path, original) as idle:
                    if not idle:
                        reason = "in use or changed"
                    elif path in _active(data_dir, log_dir):
                        reason = "active"
                    elif _identity(path.lstat()) != _identity(original):
                        reason = "changed"
                    else:
                        report["eligible"].append(path.name)
                        if apply:
                            path.unlink()
                            report["deleted"].append(path.name)
                        continue
        except FileNotFoundError:
            # A candidate may disappear; loss of the cluster marker itself is an error.
            if path.exists() or not (data_dir / "current_logfiles").is_file():
                raise
            reason = "disappeared"
        report["skipped"][path.name] = reason
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--log-dir", required=True)
    parser.add_argument("--data-dir", required=True)
    parser.add_argument("--apply", action="store_true", help="unlink eligible files (default: preview)")
    args = parser.parse_args(argv)
    try:
        result = prune(args.log_dir, args.data_dir,
                       today=dt.datetime.now(SHANGHAI).date(), apply=args.apply)
    except (OSError, ValueError, RuntimeError, ImportError) as exc:
        print("PostgreSQL log retention: " + str(exc), file=sys.stderr)
        return 1
    print(json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())

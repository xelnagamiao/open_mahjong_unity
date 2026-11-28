"""Exercise the retention policy only on synthetic files; no running PostgreSQL is needed."""
from contextlib import contextmanager
from datetime import date, datetime, timedelta, timezone
import errno
import importlib.util
import os
from pathlib import Path
import subprocess
import sys
from unittest.mock import patch

import pytest


SCRIPT = Path(__file__).resolve().parents[1] / 'ops' / 'postgresql' / 'prune_pg_logs.py'
SPEC = importlib.util.spec_from_file_location('prune_pg_logs', SCRIPT)
pruner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(pruner)

SHANGHAI = timezone(timedelta(hours=8))
DAY = date(2026, 11, 27)


def name(day):
    return f'postgresql-{day.isoformat()}.log'


def stamp(path, day):
    epoch = datetime(day.year, day.month, day.day, 12, 0, tzinfo=SHANGHAI).timestamp()
    os.utime(path, (epoch, epoch))


def make_file(path, day):
    path.write_bytes(b'fixture bytes\n')
    stamp(path, day)
    return path


def layout(root, today=DAY):
    logs, data = root / 'logs', root / 'data'
    logs.mkdir()
    data.mkdir()
    active = make_file(logs / name(today), today)
    (data / 'current_logfiles').write_text(f'stderr {active.as_posix()}\n', encoding='utf-8')
    return logs, data, active


def run(logs, data, *, today=DAY, apply=None, opened=None):
    @contextmanager
    def lease(_path, expected_stat):
        in_use = opened() if opened else set()
        yield (expected_stat.st_dev, expected_stat.st_ino) not in in_use

    with patch.object(pruner, '_exclusive_log', lease):
        return pruner.prune(logs, data, today=today, **({} if apply is None else {'apply': apply}))


@pytest.mark.parametrize('today', [DAY, date(2027, 1, 3), date(2028, 3, 3)])
def test_keeps_seven_calendar_dates_and_is_dry_by_default(tmp_path, today):
    logs, data, active = layout(tmp_path, today)
    files = {age: make_file(logs / name(today - timedelta(days=age)), today - timedelta(days=age))
             for age in (*range(1, 9), -1)}
    expired = {files[age].name for age in (7, 8)}
    # Don't pass apply here: accidental opt-in-by-default would delete real logs.
    plan = run(logs, data, today=today)
    assert set(plan['eligible']) == expired and not plan['deleted']
    assert all(p.is_file() for p in files.values()) and active.is_file()

    first = run(logs, data, today=today, apply=True)
    assert set(first['deleted']) == expired
    assert all(not files[age].exists() for age in (7, 8))
    assert all(files[age].is_file() for age in (*range(1, 7), -1)) and active.is_file()
    second = run(logs, data, today=today, apply=True)
    assert not second['eligible'] and not second['deleted']


def test_protects_reported_active_open_and_recently_written_old_names(tmp_path):
    logs, data, _ = layout(tmp_path)
    old = DAY - timedelta(days=30)
    active, opened, recently_written, expired = [
        make_file(logs / name(old + timedelta(days=i)), old) for i in range(4)]
    (data / 'current_logfiles').write_text(f'stderr {active.as_posix()}\n', encoding='utf-8')
    stamp(recently_written, DAY)
    inode = opened.stat()
    result = run(logs, data, apply=True, opened=lambda: {(inode.st_dev, inode.st_ino)})
    assert set(result['deleted']) == {expired.name}
    for survivor in (active, opened, recently_written):
        assert survivor.is_file() and survivor.name not in result['eligible']
        assert survivor.name in result['skipped']


def test_current_logfiles_accepts_data_directory_relative_path(tmp_path):
    logs, data, _ = layout(tmp_path)
    old = DAY - timedelta(days=15)
    active = make_file(logs / name(old), old)
    expired = make_file(logs / name(old - timedelta(days=1)), old)
    # PostgreSQL resolves relative log paths against its data directory, not the cleanup process CWD.
    relative = Path(os.path.relpath(active, data)).as_posix()
    (data / 'current_logfiles').write_text(f'stderr {relative}\n', encoding='utf-8')
    result = run(logs, data, apply=True)
    assert active.is_file() and set(result['deleted']) == {expired.name}


def test_only_strict_date_named_regular_files_at_top_level_are_managed(tmp_path):
    logs, data, _ = layout(tmp_path)
    old = DAY - timedelta(days=30)
    unmanaged = [make_file(logs / n, old) for n in (
        'pgsql.log', 'postgresql-2026-02-30.log', 'postgresql-2026-1-02.log',
        'postgresql-2026-01-02.log.gz', 'postgresql-2026-01-02.log.bak', 'x-postgresql-2026-01-02.log')]
    dated_directory = logs / name(old + timedelta(days=1))
    dated_directory.mkdir()
    nested = make_file(dated_directory / name(old), old)
    wal = data / 'pg_wal'
    wal.mkdir()
    outside = make_file(wal / name(old), old)
    expired = make_file(logs / name(old), old)
    result = run(logs, data, apply=True)
    assert set(result['deleted']) == {expired.name}
    assert all(p.read_bytes() == b'fixture bytes\n' for p in (*unmanaged, nested, outside))


@pytest.mark.parametrize('subdir', ['pg_wal', 'base'])
def test_even_a_bad_collector_setting_cannot_target_database_files(tmp_path, subdir):
    _, data, _ = layout(tmp_path)
    unsafe = data / subdir
    unsafe.mkdir()
    current = make_file(unsafe / name(DAY), DAY)
    old = DAY - timedelta(days=30)
    expired = make_file(unsafe / name(old), old)
    (data / 'current_logfiles').write_text(f'stderr {current.as_posix()}\n', encoding='utf-8')
    with pytest.raises(ValueError):
        run(unsafe, data, apply=True)
    assert expired.is_file() and current.is_file()


@pytest.mark.parametrize('kind', ['symlink', 'hardlink'])
def test_does_not_follow_or_unlink_links(tmp_path, kind):
    logs, data, _ = layout(tmp_path)
    old = DAY - timedelta(days=90)
    outside = make_file(tmp_path / 'unrelated-file', old)
    alias = logs / name(old)
    try:
        if kind == 'symlink':
            alias.symlink_to(outside)
        else:
            os.link(outside, alias)
    except (NotImplementedError, OSError) as exc:
        pytest.skip(f'{kind} creation unavailable on this host: {exc}')
    expired = make_file(logs / name(old + timedelta(days=1)), old)
    assert set(run(logs, data, apply=True)['deleted']) == {expired.name}
    assert alias.exists() and outside.read_bytes() == b'fixture bytes\n'


@pytest.mark.parametrize('problem', ['index_missing', 'index_empty', 'target_missing', 'malformed', 'lease_unavailable'])
def test_uncertain_active_files_never_allow_deletion(tmp_path, problem):
    logs, data, active = layout(tmp_path)
    old = DAY - timedelta(days=30)
    victim = make_file(logs / name(old), old)
    index = data / 'current_logfiles'
    opened = set
    if problem == 'index_missing':
        index.unlink()
    elif problem == 'index_empty':
        index.write_bytes(b'')
    elif problem == 'target_missing':
        active.unlink()
    elif problem == 'malformed':
        index.write_bytes(b'stderr\n')
    else:
        def opened():
            raise PermissionError('unable to verify exclusive access in test')
    try:
        result = run(logs, data, apply=True, opened=opened)
    except (OSError, RuntimeError, ValueError):
        pass  # Either an explicit failure or an empty plan is a legitimate fail-closed result.
    else:
        assert not result['deleted']
    assert victim.read_bytes() == b'fixture bytes\n'


@pytest.mark.parametrize('transition', ['rotated_to_candidate', 'marker_corrupted', 'marker_removed', 'replaced_inode'])
def test_candidate_and_collector_are_rechecked_after_the_lease(tmp_path, transition):
    logs, data, _ = layout(tmp_path)
    old = DAY - timedelta(days=31)
    candidate = make_file(logs / name(old), old)
    original_inode = candidate.stat().st_ino
    marker = data / 'current_logfiles'
    entered = []

    @contextmanager
    def rotate_while_acquiring(path, previous_stat):
        assert path == candidate and previous_stat.st_ino == original_inode
        entered.append(path)
        if transition == 'rotated_to_candidate':
            marker.write_text(f'stderr {candidate.as_posix()}\n', encoding='utf-8')
        elif transition == 'marker_corrupted':
            marker.write_bytes(b'invalid collector marker\n')
        elif transition == 'marker_removed':
            marker.unlink()
        else:
            other = make_file(tmp_path / 'replacement', old)
            assert other.stat().st_ino != original_inode
            os.replace(other, candidate)
        yield True

    with patch.object(pruner, '_exclusive_log', rotate_while_acquiring):
        if transition in ('marker_removed', 'marker_corrupted'):
            with pytest.raises((OSError, RuntimeError, ValueError)):
                pruner.prune(logs, data, today=DAY, apply=True)
        else:
            result = pruner.prune(logs, data, today=DAY, apply=True)
            assert not result['deleted'] and candidate.name in result['skipped']
    assert entered == [candidate] and candidate.read_bytes() == b'fixture bytes\n'


@pytest.mark.skipif(not sys.platform.startswith('linux'), reason='requires Linux F_SETLEASE')
def test_real_kernel_lease_retains_open_reader_and_releases_idle_file(tmp_path):
    import fcntl
    import select

    if not hasattr(fcntl, 'F_SETLEASE'):
        pytest.skip('F_SETLEASE unavailable on this Python')
    logs, data, _ = layout(tmp_path)
    old = DAY - timedelta(days=30)
    held = make_file(logs / name(old), old)
    idle = make_file(logs / name(old - timedelta(days=1)), old)
    # Prove the filesystem really supports a write lease before testing production behavior.
    with idle.open('r+b') as f:
        try:
            fcntl.fcntl(f, fcntl.F_SETLEASE, fcntl.F_WRLCK)
        except OSError as e:
            if e.errno in (errno.EACCES, errno.EPERM, errno.EINVAL, errno.ENOSYS, errno.EOPNOTSUPP):
                pytest.skip(f'kernel/filesystem disallows leases: {e}')
            raise
        fcntl.fcntl(f, fcntl.F_SETLEASE, fcntl.F_UNLCK)

    child_code = "import sys\nwith open(sys.argv[1], 'rb'):\n print('opened', flush=True)\n sys.stdin.buffer.read(1)"
    with subprocess.Popen([sys.executable, '-I', '-B', '-c', child_code, str(held)],
                          stdin=subprocess.PIPE, stdout=subprocess.PIPE) as child:
        try:
            assert select.select([child.stdout], [], [], 10)[0] and child.stdout.readline() == b'opened\n'
            result = pruner.prune(logs, data, today=DAY, apply=True)
            assert set(result['deleted']) == {idle.name} and held.is_file()
        finally:
            child.communicate(input=b'x', timeout=5)
    assert child.returncode == 0
    assert set(pruner.prune(logs, data, today=DAY, apply=True)['deleted']) == {held.name}

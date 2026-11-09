from .android_version import client_version, framework_mismatch


class Config:
    release_version = 32
    base_version = 1


def test_versions_are_the_existing_release_and_one_base_setting():
    assert client_version(Config) == {"baseVersion": 1, "releaseVersion": 32}


def test_framework_check_does_not_change_windows_or_webgl():
    for value in ({}, {"base_version": 0}, {"base_version": None}):
        assert not framework_mismatch(value, Config)
    assert not framework_mismatch({"base_version": 1}, Config)
    assert framework_mismatch({"base_version": 2}, Config)


def test_older_private_config_defaults_to_first_framework():
    class Old:
        release_version = 32
    assert client_version(Old) == {"baseVersion": 1, "releaseVersion": 32}

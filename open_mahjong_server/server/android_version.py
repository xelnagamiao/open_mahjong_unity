def client_version(config):
    """Public Android bootstrap information; no database or login is required."""
    return {"baseVersion": getattr(config, "base_version", 1), "releaseVersion": config.release_version}


def framework_mismatch(message, config):
    # Only clients explicitly declaring an Android framework participate.
    # Existing Windows/WebGL clients omit it or send zero.
    base = message.get("base_version", 0)
    return isinstance(base, int) and not isinstance(base, bool) and base > 0 and base != getattr(config, "base_version", 1)

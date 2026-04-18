# Security Policy

## Supported versions

Only the latest published major version of the `ImageResize` NuGet package receives security fixes.

| Version | Supported |
| ------- | --------- |
| 3.x     | ✅        |
| < 3.0   | ❌        |

## Reporting a vulnerability

Please **do not** open public GitHub issues for security problems.

Report privately via one of:

- GitHub Security Advisories: <https://github.com/YodasMyDad/ImageResize/security/advisories/new>
- Email: lee@aptitude.co.uk

Include:

- Affected version(s) and platform
- A minimal reproduction (PoC, command, or crafted image)
- Your assessment of impact (RCE, DoS, information disclosure, path traversal, etc.)

We aim to acknowledge reports within 3 business days and ship a fix or mitigation within 30 days for high-severity issues. You will be credited in the release notes unless you prefer to remain anonymous.

## Hardening notes for consumers

The library exposes a file-system-backed cache and decodes untrusted images. Consumers should:

- Keep `ImageResizeOptions.Bounds` set to sensible values — never accept unbounded width/height from request queries.
- Keep `ImageResizeOptions.MaxSourceBytes` set (defaults to 256 MiB) to reject decompression bombs before they reach the decoder.
- Ensure the middleware is registered **before** `UseStaticFiles()` so resizes are not shadowed by raw file serving.
- Store `CacheRoot` on a dedicated volume with a quota; the `Cache.MaxCacheBytes` cap is a soft limit, not a hard one.
- Do not expose the cache directory over HTTP unless necessary.

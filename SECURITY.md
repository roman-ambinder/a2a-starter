# Security policy

## Reporting a vulnerability

Please report vulnerabilities privately through GitHub private vulnerability reporting:

1. Open the [Security tab](https://github.com/roman-ambinder/a2a-starter/security) of this repository.
2. Choose **Report a vulnerability** and fill in the form.

Do not open a public issue, pull request or discussion for a vulnerability. Include the affected package IDs and versions,
the .NET SDK version, and steps or a small project that reproduces the problem.

### Response targets

These are targets, not guarantees. This is a single-maintainer project.

- Acknowledge a report within 7 days.
- Give a first assessment (accepted, needs more information, or not a vulnerability) within 14 days.
- Publish a fix and a GitHub security advisory as soon as a fix is ready, and credit the reporter if they want that.

## Supported versions

- Before `1.0.0`: only the latest preview receives fixes. Upgrade to the newest preview to get a fix.
- From `1.0.0`: the latest minor of the latest major receives fixes. The previous minor receives security fixes for three
  months after the next minor ships.

The target framework is `net10.0` only.

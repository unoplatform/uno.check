---
uid: UnoCheck.Configuration.Windows.RestrictedEnvironments
---

# Running without elevation on Windows

In restricted environments, it may be required to run uno-check to determine what needs to be installed without privileges elevation.

In order to do so, use the following command:

```bash
cmd /c "set __COMPAT_LAYER=RUNASINVOKER && uno-check"
```

---
uid: UnoCheck.Configuration.CI
---
<!--markdownlint-disable MD025 -->
# Using Uno.Check in a CI environment

It is possible to run Uno.Check to setup your build environment in a repeatable way by using the following commands:

# [**Windows**](#tab/windows)

```bash
dotnet tool install --global Uno.Check --version 1.29.4
uno-check -v --ci --non-interactive --fix --skip vswin --skip androidemulator --skip androidsdk
```

# [**macOS**](#tab/macos)

```bash
dotnet tool install --global Uno.Check --version 1.29.4
uno-check -v --ci --non-interactive --fix --skip xcode --skip androidemulator --skip androidsdk
```

# [**Linux**](#tab/linux)

```bash
dotnet tool install --global Uno.Check --version 1.29.4
uno-check -v --ci --non-interactive --fix --skip androidemulator
```

---

[!INCLUDES [Version-Management-Updating](../includes/version-management-updating-inline.md)]

[!INCLUDES [dotnet-search-package](../includes/dotnet-search-package-inline.md)]

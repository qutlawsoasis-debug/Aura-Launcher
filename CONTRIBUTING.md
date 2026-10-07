# 🤝 Contributing to Aura Launcher

Thank you for your interest in contributing to **Aura Launcher**! We welcome bug fixes, performance improvements, feature proposals, and translations.

---

## 📋 Code of Conduct

We are committed to providing a welcoming, diverse, and inclusive environment. Please treat all contributors and community members with mutual respect and professionalism.

---

## 🛠️ Getting Started

### 1. Prerequisites
- **Operating System**: Windows 10 or Windows 11 (64-bit)
- **.NET SDK**: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- **IDE / Editor**: Visual Studio 2022 (v17.8+), JetBrains Rider, or VS Code with C# Dev Kit
- **Git**: Installed and configured

### 2. Fork & Clone
```bash
git clone https://github.com/<your-username>/Aura-Launcher.git
cd Aura-Launcher
```

### 3. Build & Test
```powershell
# Restore NuGet dependencies
dotnet restore

# Build in Debug or Release configuration
dotnet build

# Run unit tests
dotnet test tests/AuraLauncher.Tests/AuraLauncher.Tests.csproj --filter "Category!=Integration"
```

---

## 🌿 Branching Strategy & Workflow

1. Create a feature or bugfix branch off `main`:
   ```bash
   git checkout -b feature/smooth-animations
   # or
   git checkout -b fix/lobby-presence
   ```
2. Make concise, well-documented changes adhering to the repository coding style.
3. Verify that all existing unit tests pass before submitting.
4. Commit with descriptive Conventional Commits messages:
   - `feat: add quick reconnect button`
   - `fix: prevent button jumping on copy event`
   - `perf: optimize memory footprint during 3D skin render`
   - `docs: update quickstart guide`

---

## 🎨 Code Style Guidelines

- **C#**:
  - Follow standard .NET naming conventions (PascalCase for classes/methods, camelCase with `_` prefix for private fields).
  - Use file-scoped namespaces where appropriate.
  - Strive for clean MVVM separation (Views in `Views/`, ViewModels in `ViewModels/`, Services behind interfaces in `Services/`).
- **WPF / XAML**:
  - Prefer styles and dynamic resource references from `Styles/UiTheme.xaml`.
  - Avoid hardcoded magic sizes that cause text clipping across different display DPIs or system scaling.
  - Implement smooth easing curves (`CubicEase`, `QuadraticEase`) for user-facing transitions.

---

## 🚀 Submitting a Pull Request

1. Push your branch to your fork.
2. Open a Pull Request against `main` on the official repository.
3. Fill out the PR template describing the problem solved and the changes made.
4. Link any related issues (`Fixes #123`).

Thank you for helping make Aura Launcher the best Minecraft launcher experience!

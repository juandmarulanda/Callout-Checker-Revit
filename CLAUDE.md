# CalloutCheck (Revit add-in)

- C# / WPF add-in for Revit 2025, 2026 (.NET 8) and 2027 (.NET 10). Single project `src/CalloutCheck/CalloutCheck.csproj`; pick the version with `-p:RevitVersion=2025|2026|2027`.
- Revit API comes from the `Nice3point.Revit.Api.*` NuGet packages (no Revit install needed to build). Building needs Windows; on Linux rely on the GitHub Actions workflow (`.github/workflows/build.yml`), which builds all three versions and, on `main`, publishes `CalloutCheck-Installer.zip` to the `installer` branch.
- The window is modeless: every model read/write must go through `App.Tasks.Run(...)` (ExternalEvent queue in `Core/RevitTaskQueue.cs`).
- Plugin UI text is in English. Author shown in the footer: Juan Marulanda.
- The user communicates in Spanish.

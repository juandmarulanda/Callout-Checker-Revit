<p align="center"><img src="assets/icon-256.png" width="110" alt="CalloutCheck icon"></p>

# CalloutCheck for Revit 2025 · 2026 · 2027

A Revit add-in that answers one question before you issue a set: **which details on my detail sheets have I not called out on the plans yet?**

Author: **Juan Marulanda**

## Features

### Callout Audit (main tab)
- Finds every **drafting view** placed on your detail sheets (any number of sheets — by default every sheet whose number or name contains `DETAIL`; change the keyword or tick sheets by hand).
- Scans the model for reference callouts, reference sections and reference elevations that point at those drafting views.
- **Missing callouts** list: each drafting view that is not referenced anywhere, with its detail/sheet number (e.g. `3 / A503`) and a **Copy** button that puts the view name on the clipboard, ready to paste into a new callout's reference.
- **Called out** list: each referenced drafting view with a **count** of how many callouts point to it, and a **Where?** button that opens a small window listing the sheet numbers (and host views) where you called it out. **Show** zooms to that callout in Revit.
- Click any view name to **open the drafting view**.
- Option *Only count callouts in views placed on sheets* (on by default) so a callout in a working view that never makes it to a sheet doesn't hide a missing one.
- Search box to filter both lists.

### Renumber Views (second tab)
- Open a sheet, click **Pick order**, then click its views in the order you want (ESC to finish). They get detail numbers 1, 2, 3… in that order.
- Fine-tune with the ▲ ▼ arrows, choose the start number, and exclude views that should keep their number.
- Applied in one transaction (one Undo) and via temporary numbers, so swapping numbers never collides.

The window is modeless: keep it open while you place callouts and hit **Refresh**.

## Build

Requires the .NET 8 SDK (Revit 2025/2026) and the .NET 10 SDK (Revit 2027) on Windows. Revit API references come from the `Nice3point.Revit.Api.*` NuGet packages, so Revit doesn't need to be installed to build.

```powershell
.\build.ps1                    # builds 2025, 2026 and 2027
.\build.ps1 -Versions 2026     # one version
.\install.ps1                  # copies the build into %AppData%\Autodesk\Revit\Addins\<version>
```

Or with dotnet directly: `dotnet build src/CalloutCheck/CalloutCheck.csproj -c Release -p:RevitVersion=2027`.

Every push also builds all three versions on GitHub Actions; the zipped add-in for each version is attached to the run as an artifact.

## Install (easiest)

Download `CalloutCheck-Installer.zip` from the [`installer` branch](../../blob/installer/CalloutCheck-Installer.zip) (rebuilt automatically on every push to `main`), unzip it, close Revit and double-click **`Install.bat`**. It installs for Revit 2025, 2026 and 2027. `Uninstall.bat` removes it.

## Manual install

Copy into `%AppData%\Autodesk\Revit\Addins\<version>\`:

```
CalloutCheck.addin
CalloutCheck\CalloutCheck.dll
```

The button appears on the **CalloutCheck** ribbon tab.

## How "called out" is detected

A drafting view counts as called out when a reference callout/section/elevation targets it (`ReferenceableViewUtils.GetReferencedViewId`). The host view of each callout is matched to sheets through its viewport, including sheets that show its dependent views. Callouts placed on other drafting views (detail-to-detail references) count too.

## Icon

`assets/make_icon.py` regenerates the icon (coffee cup in profile inside a Revit callout) with Pillow.

# Third-party notices

PC Companion is licensed under the [MIT License](LICENSE). The installer also includes the following components, under their own licenses. Versions are set in `Directory.Packages.props` and the `packages.lock.json` files.

| Component | License |
|---|---|
| [.NET runtime](https://github.com/dotnet/runtime) | [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) |
| [Windows App SDK](https://github.com/microsoft/WindowsAppSDK): `Microsoft.WindowsAppSDK.WinUI`, `.Foundation`, `.InteractiveExperiences` and `.Base` | Microsoft Software License Terms for the Windows App SDK, included in each package |
| [WebView2 SDK](https://www.nuget.org/packages/Microsoft.Web.WebView2) (`Microsoft.Web.WebView2`), used by WinUI | BSD-style license from Microsoft, included in the package |
| Windows SDK projection for .NET (`Microsoft.Windows.SDK.NET`, `WinRT.Runtime`) | [Windows SDK license terms](https://aka.ms/WinSDKLicenseURL) |
| [Windows Community Toolkit](https://github.com/CommunityToolkit/Windows): `CommunityToolkit.WinUI.Controls.SettingsControls`, `.Extensions`, `.Helpers`, `.Triggers` and `CommunityToolkit.Common` | [MIT](https://github.com/CommunityToolkit/Windows/blob/main/License.md) |
| [MVVM Toolkit](https://github.com/CommunityToolkit/dotnet) (`CommunityToolkit.Mvvm`) | [MIT](https://github.com/CommunityToolkit/dotnet/blob/main/License.md) |
| [Microsoft.Extensions](https://github.com/dotnet/runtime): `DependencyInjection`, `Logging`, `Options` and `Primitives` | [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) |
| [Serilog](https://github.com/serilog/serilog), [Serilog.Extensions.Logging](https://github.com/serilog/serilog-extensions-logging) and [Serilog.Sinks.File](https://github.com/serilog/serilog-sinks-file) | [Apache 2.0](https://www.apache.org/licenses/LICENSE-2.0) |

Tools used only to build PC Companion, such as WiX and CsWin32, aren't installed and aren't listed.

The full license texts are in [ThirdPartyNotices.txt](ThirdPartyNotices.txt), which is installed next to the app. When a package that ships with the app is added, add it to both files; a test fails when a shipped package is missing from `ThirdPartyNotices.txt`.

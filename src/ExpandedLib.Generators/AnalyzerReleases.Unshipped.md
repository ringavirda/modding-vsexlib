; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
EXLIB0001 | ExpandedLib.Config | Error | ExConfigGenerator, [ExRecipeProfile] without [ExConfigRegister]
EXLIB0002 | ExpandedLib.Config | Error | ExConfigGenerator, [ExRecipeProfile] shape is invalid
EXLIB0003 | ExpandedLib.Lang | Warning | ExLangKeyGenerator, AssetDomain has no matching lang file
EXLIB0004 | ExpandedLib.Lang | Warning | ExLangKeyGenerator, lang keys collide on their sanitised member name

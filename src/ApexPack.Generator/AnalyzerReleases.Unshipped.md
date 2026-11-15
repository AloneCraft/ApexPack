; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
APX001 | ApexPack | Error | Type must be partial
APX002 | ApexPack | Error | Containing type must be partial
APX003 | ApexPack | Warning | Formatter resolved at runtime
APX004 | ApexPack | Warning | Struct is serialized by raw memory copy
APX005 | ApexPack | Error | No usable constructor
APX006 | ApexPack | Error | Constructor parameter does not match a member
APX007 | ApexPack | Error | Abstract type without [ApexUnion]
APX008 | ApexPack | Error | Versioning is not supported
APX009 | ApexPack | Error | Instance [ApexOnDeserializing] needs early construction
APX010 | ApexPack | Error | Invalid [ApexOrder]
APX011 | ApexPack | Error | Duplicate union tag
APX012 | ApexPack | Error | Invalid collection type
APX013 | ApexPack | Error | Unsupported type

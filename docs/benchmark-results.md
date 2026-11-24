# Benchmark results by scenario

Positive percentages mean ApexPack takes less time or produces fewer bytes than MemoryPack.
Negative percentages mean ApexPack takes more time or produces more bytes. 0% means no change at the displayed precision.
Write time means serialization. Read time means deserialization.

[Raw measurements and memory allocations (CSV)](data/runtime-benchmarks.csv)

## Basic types and strings

| Scenario | .NET 9.0.23 write time | .NET 9.0.23 read time | .NET 9.0.23 output size | .NET 10.0.15 write time | .NET 10.0.15 read time | .NET 10.0.15 output size | .NET 11.0.0 write time | .NET 11.0.0 read time | .NET 11.0.0 output size |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Basic Primitives (int, long, double, guid) | +2.2% | +81.2% | +12.5% | -0.1% | +75.8% | +12.5% | +0.4% | +75.9% | +12.5% |
| Unmanaged Struct Array (10,000 items) | -17.3% | +64.5% | 0% | +15.4% | +6.3% | 0% | +12.5% | +1.5% | 0% |
| Class with Strings &amp; Primitives | +27.8% | +41.4% | +18.4% | +31.1% | +43.3% | +18.4% | +30.2% | +46.3% | +18.4% |
| Strings (ASCII / Japanese / Mixed) | +51.8% | +55.4% | +16.2% | +55.6% | +59.8% | +16.2% | +53.6% | +59.1% | +16.2% |
| Unmanaged Struct with Padding | +20.3% | +62.1% | +37.5% | -0.1% | +5.1% | +37.5% | +5.3% | +3.7% | +37.5% |

## Inheritance and nested objects

| Scenario | .NET 9.0.23 write time | .NET 9.0.23 read time | .NET 9.0.23 output size | .NET 10.0.15 write time | .NET 10.0.15 read time | .NET 10.0.15 output size | .NET 11.0.0 write time | .NET 11.0.0 read time | .NET 11.0.0 output size |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Abstract Class Polymorphism (Circle/Rect) | +45.4% | +37.7% | +24.1% | +44.5% | +25.3% | +24.1% | +39.6% | +24.3% | +24.1% |
| Arrays, Lists, Dictionaries &amp; Shapes | +58.4% | -0.3% | +41.3% | +53.9% | +6.9% | +41.3% | +58.4% | +4.0% | +41.3% |
| Nested Objects (Order &gt; Customer &gt; Address) | +54.4% | +21.4% | +23.0% | +25.1% | +24.5% | +23.0% | +26.4% | +30.1% | +23.0% |
| List of Objects (100 items) | +21.7% | +31.3% | +23.7% | +19.1% | +34.5% | +23.7% | +20.5% | +36.5% | +23.7% |

## Large payloads and .NET 11 features

| Scenario | .NET 9.0.23 write time | .NET 9.0.23 read time | .NET 9.0.23 output size | .NET 10.0.15 write time | .NET 10.0.15 read time | .NET 10.0.15 output size | .NET 11.0.0 write time | .NET 11.0.0 read time | .NET 11.0.0 output size |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Sparse Nullable &amp; Bool DTO | +36.1% | +23.4% | +78.2% | +36.7% | +18.1% | +78.2% | +39.6% | +19.2% | +78.2% |
| 16 Bool Flags DTO | +43.1% | +48.8% | +66.7% | +43.0% | +49.8% | +66.7% | +48.1% | +47.5% | +66.7% |
| int?[] (1,000 items, 1/3 null) | -3.6% | -79.8% | +65.1% | +15.3% | -65.0% | +65.1% | -5.8% | -64.9% | +65.1% |
| Circular Reference Graph (100 nodes) | +81.7% | +42.3% | +56.0% | +87.2% | +41.2% | +56.0% | +86.1% | +43.7% | +56.0% |
| Large Payload &gt; 64KB (5,000 objects) | +36.4% | +31.7% | +22.4% | +30.5% | +33.7% | +22.4% | +22.7% | +23.8% | +22.4% |
| FullJoin (.NET 11, 1,500 rows) | N/A | N/A | N/A | N/A | N/A | N/A | +10.5% | -4.1% | +46.7% |
| BFloat16[] (.NET 11, 4,096 items) | N/A | N/A | N/A | N/A | N/A | N/A | -1.5% | +4.6% | 0% |
| Complex&lt;Decimal128&gt;[] (1,024 items) | N/A | N/A | N/A | N/A | N/A | N/A | -12.6% | -6.0% | 0% |

## Value types and reference types

| Scenario | .NET 9.0.23 write time | .NET 9.0.23 read time | .NET 9.0.23 output size | .NET 10.0.15 write time | .NET 10.0.15 read time | .NET 10.0.15 output size | .NET 11.0.0 write time | .NET 11.0.0 read time | .NET 11.0.0 output size |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 16-byte struct | +17.8% | +73.8% | 0% | -3.4% | +2.0% | 0% | -6.2% | +0.5% | 0% |
| 16-byte struct array (1) | +0.9% | +19.0% | +15.0% | +13.0% | +10.1% | +15.0% | -7.8% | +5.2% | +15.0% |
| 16-byte struct array (32) | -14.3% | +14.2% | +0.6% | +10.0% | +5.8% | +0.6% | -5.1% | -0.6% | +0.6% |
| 16-byte struct array (1,024) | +7.0% | +3.7% | 0% | -5.2% | +4.1% | 0% | -5.0% | +1.1% | 0% |
| 128-byte struct | +4.1% | +74.1% | 0% | +5.6% | +68.7% | 0% | -4.8% | +66.7% | 0% |
| 128-byte struct array (1) | -6.9% | +15.4% | +2.3% | -15.7% | -21.3% | +2.3% | -12.0% | +4.1% | +2.3% |
| 128-byte struct array (32) | -0.5% | -0.6% | +0.1% | -8.0% | -13.7% | +0.1% | -3.9% | +2.3% | +0.1% |
| 128-byte struct array (1,024) | -21.1% | -32.8% | 0% | +1.6% | -13.2% | 0% | -29.1% | -73.1% | 0% |
| 16-byte class | +61.8% | +53.0% | 0% | +59.0% | +41.9% | 0% | +61.2% | +30.2% | 0% |
| 16-byte class array (1) | +67.0% | +18.1% | +14.3% | +65.6% | +9.8% | +14.3% | +66.6% | +4.9% | +14.3% |
| 16-byte class array (32) | +35.4% | +40.5% | +0.5% | +55.6% | +25.7% | +0.5% | +31.1% | +27.4% | +0.5% |
| 16-byte class array (1,024) | +44.5% | +41.5% | 0% | +55.4% | +19.4% | 0% | +34.4% | +33.8% | 0% |
| 128-byte class | +38.8% | +2.3% | 0% | +47.5% | +16.8% | 0% | +47.6% | -0.4% | 0% |
| 128-byte class array (1) | +51.2% | +4.7% | +2.3% | +34.8% | +17.0% | +2.3% | +52.1% | +2.4% | +2.3% |
| 128-byte class array (32) | +0.4% | -24.1% | +0.1% | -12.2% | -2.6% | +0.1% | -7.2% | -2.4% | +0.1% |
| 128-byte class array (1,024) | -2.3% | -15.0% | 0% | -20.3% | -6.8% | 0% | -9.6% | -1.1% | 0% |
| Struct with a short string | +52.8% | +47.6% | +47.1% | +44.4% | +45.9% | +47.1% | +48.5% | +51.2% | +47.1% |
| Struct with a short string array (1) | +28.2% | +25.2% | +52.4% | +31.9% | +26.4% | +52.4% | +29.5% | +26.1% | +52.4% |
| Struct with a short string array (32) | +50.1% | +39.4% | +47.3% | +46.4% | +34.0% | +47.3% | +43.7% | +33.5% | +47.3% |
| Struct with a short string array (1,024) | +52.4% | +40.1% | +47.1% | +45.5% | +34.7% | +47.1% | +46.8% | +34.7% | +47.1% |
| Struct with a long string | +27.6% | +4.7% | +2.2% | +12.1% | +4.4% | +2.2% | +17.9% | +7.3% | +2.2% |
| Struct with a long string array (1) | +25.7% | +2.3% | +3.3% | +27.2% | +8.6% | +3.3% | +16.2% | -0.9% | +3.3% |
| Struct with a long string array (32) | +16.2% | -7.3% | +2.3% | +11.6% | -5.9% | +2.3% | +19.9% | +9.2% | +2.3% |
| Struct with a long string array (1,024) | +10.4% | -3.5% | +2.2% | +10.1% | -12.2% | +2.2% | +15.9% | +9.6% | +2.2% |
| Class with a short string | +51.9% | +37.1% | +41.2% | +38.7% | +25.3% | +41.2% | +32.3% | +28.4% | +41.2% |
| Class with a short string array (1) | -9.8% | +20.7% | +47.6% | -22.3% | +15.1% | +47.6% | -24.8% | +8.9% | +47.6% |
| Class with a short string array (32) | +27.8% | +33.8% | +41.4% | +31.4% | +31.0% | +41.4% | +11.0% | +29.2% | +41.4% |
| Class with a short string array (1,024) | +26.3% | +34.7% | +41.2% | +30.9% | +33.9% | +41.2% | +13.3% | +32.5% | +41.2% |
| Class with a long string | +29.6% | +13.7% | +1.9% | +17.3% | -7.3% | +1.9% | +21.2% | +1.2% | +1.9% |
| Class with a long string array (1) | +6.8% | +13.1% | +2.9% | +23.1% | -0.4% | +2.9% | -29.8% | +6.0% | +2.9% |
| Class with a long string array (32) | +9.1% | +3.4% | +1.9% | -1.0% | -0.3% | +1.9% | +8.7% | +9.4% | +1.9% |
| Class with a long string array (1,024) | +10.4% | +4.1% | +1.9% | +4.1% | -4.1% | +1.9% | +7.1% | -23.6% | +1.9% |

## Arrays, lists, and dictionaries

| Scenario | .NET 9.0.23 write time | .NET 9.0.23 read time | .NET 9.0.23 output size | .NET 10.0.15 write time | .NET 10.0.15 read time | .NET 10.0.15 output size | .NET 11.0.0 write time | .NET 11.0.0 read time | .NET 11.0.0 output size |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| int[] (32) | +13.8% | +19.4% | +2.3% | +18.7% | -12.6% | +2.3% | +18.8% | +4.3% | +2.3% |
| List&lt;int&gt; (32) | +25.2% | +15.5% | +2.3% | +33.8% | -11.8% | +2.3% | +24.1% | +3.9% | +2.3% |
| LinkedList&lt;int&gt; (32) | +68.3% | +10.2% | +2.3% | +68.3% | +9.9% | +2.3% | +62.0% | +8.4% | +2.3% |
| 16-byte class[] (32) | +39.9% | +35.1% | +0.5% | +30.1% | +30.8% | +0.5% | +40.5% | +49.5% | +0.5% |
| List&lt;16-byte class&gt; (32) | +44.0% | +39.6% | +0.5% | +25.7% | +30.6% | +0.5% | +26.2% | +28.9% | +0.5% |
| LinkedList&lt;16-byte class&gt; (32) | +38.7% | +6.3% | +0.5% | +33.0% | -13.3% | +0.5% | +40.6% | -6.5% | +0.5% |
| Dictionary&lt;int, int&gt; (32) | +15.9% | +2.1% | +1.2% | -4.5% | +10.0% | +1.2% | +9.2% | +4.4% | +1.2% |
| SortedDictionary&lt;int, int&gt; (32) | -2.1% | -0.7% | +1.2% | -6.9% | +3.1% | +1.2% | -76.4% | +9.1% | +1.2% |
| SortedList&lt;int, int&gt; (32) | +30.1% | -6.2% | +1.2% | +0.3% | -10.5% | +1.2% | +10.9% | +9.7% | +1.2% |
| Dictionary&lt;int, 16-byte class&gt; (32) | +60.9% | +12.6% | +0.4% | +53.4% | +5.4% | +0.4% | +52.6% | +6.7% | +0.4% |
| SortedDictionary&lt;int, 16-byte class&gt; (32) | +43.2% | +3.2% | +0.4% | +42.2% | +8.5% | +0.4% | +41.4% | +2.6% | +0.4% |
| SortedList&lt;int, 16-byte class&gt; (32) | +42.2% | +3.1% | +0.4% | +51.1% | -13.6% | +0.4% | +52.3% | -15.0% | +0.4% |
| int[] (1,024) | +5.7% | +2.5% | 0% | +3.4% | -2.3% | 0% | +11.1% | -7.7% | 0% |
| List&lt;int&gt; (1,024) | +20.7% | -0.2% | 0% | +2.2% | -1.8% | 0% | +18.5% | +6.2% | 0% |
| LinkedList&lt;int&gt; (1,024) | +76.3% | -3.4% | 0% | +30.6% | -5.6% | 0% | +34.1% | -3.2% | 0% |
| 16-byte class[] (1,024) | +51.2% | +47.3% | 0% | +41.2% | +32.6% | 0% | +58.1% | +21.7% | 0% |
| List&lt;16-byte class&gt; (1,024) | +38.5% | +39.4% | 0% | +43.6% | +29.3% | 0% | +46.3% | +35.1% | 0% |
| LinkedList&lt;16-byte class&gt; (1,024) | +47.8% | +10.1% | 0% | +48.1% | +1.8% | 0% | +43.1% | -6.8% | 0% |
| Dictionary&lt;int, int&gt; (1,024) | +5.6% | +8.5% | 0% | -85.0% | -5.3% | 0% | +10.5% | +8.3% | 0% |
| SortedDictionary&lt;int, int&gt; (1,024) | +0.8% | +4.6% | 0% | -18.9% | -0.8% | 0% | -35.7% | +2.8% | 0% |
| SortedList&lt;int, int&gt; (1,024) | +43.2% | -0.8% | 0% | +3.3% | -2.0% | 0% | +14.8% | +6.3% | 0% |
| Dictionary&lt;int, 16-byte class&gt; (1,024) | +62.4% | +8.9% | 0% | +56.3% | +7.1% | 0% | +65.7% | +13.6% | 0% |
| SortedDictionary&lt;int, 16-byte class&gt; (1,024) | +45.0% | -6.9% | 0% | +41.8% | -7.4% | 0% | +48.3% | +1.1% | 0% |
| SortedList&lt;int, 16-byte class&gt; (1,024) | +49.0% | +10.8% | 0% | +54.8% | -0.4% | 0% | +52.7% | +15.4% | 0% |

## Trees, shared references, and cycles

| Scenario | .NET 9.0.23 write time | .NET 9.0.23 read time | .NET 9.0.23 output size | .NET 10.0.15 write time | .NET 10.0.15 read time | .NET 10.0.15 output size | .NET 11.0.0 write time | .NET 11.0.0 read time | .NET 11.0.0 output size |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Chain (100 nodes) | +89.2% | +37.5% | +55.8% | +87.6% | +44.4% | +55.8% | +88.7% | +43.7% | +55.8% |
| Binary tree (100 nodes) | +84.4% | +42.1% | +53.1% | +80.9% | +43.7% | +53.1% | +83.6% | +50.6% | +53.1% |
| Shared references (100 nodes) | +80.2% | +40.8% | +53.7% | +87.0% | +40.6% | +53.7% | +84.2% | +39.3% | +53.7% |
| Cycle (100 nodes) | +83.8% | +42.9% | +55.8% | +88.0% | +39.2% | +55.8% | +88.0% | +41.8% | +55.8% |
| Chain (1,000 nodes) | +99.8% | +39.6% | +58.9% | +99.8% | +40.3% | +58.9% | +99.7% | +2.3% | +58.9% |
| Binary tree (1,000 nodes) | +83.7% | +48.9% | +56.4% | +83.5% | +49.3% | +56.4% | +83.6% | +54.6% | +56.4% |
| Shared references (1,000 nodes) | +99.7% | +44.2% | +54.2% | +99.6% | +38.3% | +54.2% | +99.7% | -4.5% | +54.2% |
| Cycle (1,000 nodes) | +99.7% | +40.4% | +58.9% | +99.8% | +42.7% | +58.9% | +99.7% | +3.3% | +58.9% |

## Nullable references

| Scenario | .NET 9.0.23 write time | .NET 9.0.23 read time | .NET 9.0.23 output size | .NET 10.0.15 write time | .NET 10.0.15 read time | .NET 10.0.15 output size | .NET 11.0.0 write time | .NET 11.0.0 read time | .NET 11.0.0 output size |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Required string, all values present | +32.1% | +34.8% | +41.2% | +29.7% | +32.9% | +41.2% | +31.9% | +31.9% | +41.2% |
| Nullable string, all values present | +24.5% | +32.0% | +41.2% | +25.1% | +41.8% | +41.2% | +28.1% | +39.0% | +41.2% |
| Nullable string, 33% null | +38.8% | +27.8% | +41.9% | +39.1% | +26.5% | +41.9% | +36.0% | +38.8% | +41.9% |
| Nullable string, 100% null | +51.2% | +31.4% | +44.4% | +62.5% | +30.0% | +44.4% | +46.6% | +46.9% | +44.4% |
| Required child object, all values present | +45.4% | +33.4% | +5.6% | +45.0% | +38.4% | +5.6% | +43.8% | +39.9% | +5.6% |
| Nullable child object, all values present | +34.3% | +31.9% | +5.6% | +42.7% | +32.8% | +5.6% | +40.8% | +38.6% | +5.6% |

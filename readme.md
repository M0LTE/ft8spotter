# ft8spotter
.NET Core console app to provide coloured and filtered displays of FT8 decoded messages, by monitoring udp://localhost:2237

Compares callsigns to your Cloudlog database, regardless of whether the decode is a CQ or not.

You can optionally enable highlighting of new grid squares as well as the default DXCCs

# Prerequisites
- .NET Core 2.1 SDK and/or runtime, not sure ([download page](https://dotnet.microsoft.com/download/dotnet-core/2.1))
- Any OS that .NET Core 2.1 runs on. Built on OS X, should work fine on Windows and Linux too.
- Recent Cloudlog install + API key for it (from the Admin menu)

# Usage
## One-off setup
```
git clone https://github.com/M0LTE/ft8spotter.git
cd ft8spotter-master/ft8spotter
dotnet run
```
Then follow the prompt in order to set your Cloudlog instance URL
## Day-to-day
By default ft8spotter selects the 20m band, and only shows needed DXCC decodes. To run in this manner:
```
cd ft8spotter-master/ft8spotter
dotnet run
```
You can pass other options to select a different band with --band, for example --6m or --40m
You can also choose to display all needed grids with --grids or display every decode with --all
For example, selecting 6m and showing all needed grid decodes:
```
cd ft8spotter-master/ft8spotter
dotnet run --6m --grids
```
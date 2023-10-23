# UE4MarketplacePluginAssist

*Works with UE5*

If you have a code plugin on the marketplace then you probably want this.

This is a C# Application that makes it easy to build, validate, and zip your plugins ready for distribution.

It executes RunUAT.bat to build and validate the plugin. It can do every engine version in one go and tell you the results. It can also zip your plugin ready for upload.

Feel free to check out the preview video here to understand what it does in detail: https://youtu.be/72rsRnzxNog

# Download
Every update is compiled for Windows already. You can download them here: https://github.com/DrowningDragons/UE4MarketplacePluginAssist/tree/master/Binaries

# Changelog

## Version 1.1.0
* 510 is now default engine version
* VS2022 is now default version
* Added ability to modify your .uproject engine version (the digits get split, 510➜5.1.0)
* Added option to resave any unversioned packages (-run=ResavePackages -OnlyUnversioned)
* Instead of packing directly to the given directory, will create subdirectory for each engine version
* Zip file now gets created in the output directory instead of the plugin directory
* Added check for output directory being cleared successfully
* Fixed bug where wrong directory was zipped (you will no longer get .git files)
* Fixed bug where data validation incorrectly allowed checkboxes to be enabled

## Version 1.0.4
* Error checking for zip functionality
* Removed unnecessary 'using' statements
* Modified variables to fit standards better
* Modified variable access modifiers

## Version 1.0.3
* Fixed issues with versions

## Version 1.0.2
* Support for major engine versions above 4 (in preparation for UE5)
* Sade engine version change UX better

## Version 1.0.1
* Support for config.config with visual studio version setting

## Version 1.0.0
* Initial release
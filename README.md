# Tilemaps For Unity
A Tilemap library for Unity. This implementation is meant for performant tile creation and retrieval. Also aiming for optimal idle tilemap performance rendering.

### Installation
To simply add Tilemaps to your Unity project as a package:

-In the Unity Editor open up the Package Manager by going to Window -> Package Manager.

-At the top left of the Package Manager window press the plus button and press 'Add package from git URL' or similar.

-Submit ```https://github.com/Missingreference/Tilemaps.git``` as the URL and the Package Manager should add the package to your project.

### Required Packages
Be aware that this repository requires multiple package dependancies to function. Add them as packages just like the Tilemaps package.

**Core Tools For Unity** - ```https://github.com/Missingreference/Core-Tools-For-Unity.git```

A utility package. It includes many helper functions, GridArray, texture atlas and convenient functions.

**Direct Graphics** - ```https://github.com/Missingreference/Direct-Graphics-For-Unity.git?path=/UnityProject/Packages/com.elanetic.directgraphics```

Graphics rendering tool for fast texture creation and copying.

Use DEBUG mode to enable safety checks for most functions.

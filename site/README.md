# GameNight landing page

A static page (no build step): `index.html`, `favicon.svg` and the screenshots in `img/`.

Deploy on Vercel: Add New → Project → import `jpcpais01/Gamenight-nativeApp`, set
**Root Directory** to `site`, Framework Preset **Other**, leave the build command empty, Deploy.

The download buttons always point at the newest GitHub release
(`releases/latest/download/GameNight.apk` and `GameNight-Windows.zip`), so the page never needs
updating when a new build ships. iPhone opens the browser version at football-game-dusky.vercel.app.
Pushes that only touch `site/` don't start a new game build.

Placez ici l'icône de l'application au format Windows : build/icon.ico

- Format : .ico contenant idéalement les tailles 16, 32, 48, 64, 128 et 256 px.
- Si aucune icône n'est fournie, electron-builder utilise l'icône par défaut
  d'Electron (l'application se compile quand même).

Pour créer un .ico à partir d'un PNG, vous pouvez utiliser un convertisseur en
ligne ou ImageMagick :
    magick convert logo.png -define icon:auto-resize=256,128,64,48,32,16 icon.ico

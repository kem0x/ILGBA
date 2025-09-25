# 1) Find the dylib Homebrew installed
SDL_PREFIX="$(brew --prefix sdl2)"
SDL_LIB="$SDL_PREFIX/lib/libSDL2-2.0.0.dylib"
test -f "$SDL_LIB" || { echo "SDL2 not found via Homebrew"; exit 1; }

# 2) Put a copy (and a libsdl2.dylib symlink) beside your app’s executable
#    (replace ./bin/Debug/netX.Y with your actual output folder)
APP_OUT="./bin/Debug/net9.0"   # or net7.0/net6.0 etc.
mkdir -p "$APP_OUT"
cp -f "$SDL_LIB" "$APP_OUT/libSDL2-2.0.0.dylib"
( cd "$APP_OUT" && ln -sf "libSDL2-2.0.0.dylib" "libsdl2.dylib" )

# 3) (Apple Silicon) Check architecture matches your dotnet runtime
file "$APP_OUT/libSDL2-2.0.0.dylib"
dotnet --info | grep RID -n

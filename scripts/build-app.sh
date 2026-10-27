#!/bin/zsh
# 构建「歇一杯.app」。
#   ./scripts/build-app.sh            构建到 dist/歇一杯.app
#   ./scripts/build-app.sh --install  构建后安装到 /Applications 并启动
set -euo pipefail

ROOT="${0:A:h:h}"
BUILD="$ROOT/.build"
DIST="$ROOT/dist"
APP="$DIST/歇一杯.app"
INSTALL_PATH="/Applications/歇一杯.app"

cd "$ROOT"

echo "▸ 编译 release"
# 本机只有 Command Line Tools，默认的 swift-build 构建系统初始化会失败，固定用原生构建系统
mkdir -p "$BUILD"
if ! swift build -c release --build-system native --product BreakCup > "$BUILD/build-app.log" 2>&1; then
    cat "$BUILD/build-app.log"
    echo "✗ 编译失败"
    exit 1
fi
BIN="$(swift build -c release --build-system native --show-bin-path 2>/dev/null)/BreakCup"
[[ -x "$BIN" ]] || { echo "✗ 没有找到可执行文件：$BIN"; exit 1; }

echo "▸ 生成图标"
ICON_WORK="$BUILD/icon"
rm -rf "$ICON_WORK"
mkdir -p "$ICON_WORK"
swiftc -O "$ROOT/scripts/generate-icon.swift" -o "$ICON_WORK/generate-icon"
"$ICON_WORK/generate-icon" "$ICON_WORK/AppIcon.iconset" >/dev/null
iconutil -c icns "$ICON_WORK/AppIcon.iconset" -o "$ICON_WORK/AppIcon.icns"

echo "▸ 组装 .app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/BreakCup"
cp "$ROOT/Resources/Info.plist" "$APP/Contents/Info.plist"
cp "$ICON_WORK/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"
printf 'APPL????' > "$APP/Contents/PkgInfo"

echo "▸ 本机签名（ad-hoc）"
codesign --force --deep --sign - "$APP"
codesign --verify "$APP"

echo "✓ 已构建：$APP"

if [[ "${1:-}" == "--install" ]]; then
    echo "▸ 安装到 $INSTALL_PATH"
    pkill -x BreakCup >/dev/null 2>&1 && sleep 1 || true
    rm -rf "$INSTALL_PATH"
    ditto "$APP" "$INSTALL_PATH"
    open "$INSTALL_PATH"
    echo "✓ 已安装并启动，菜单栏会出现咖啡杯图标"
fi

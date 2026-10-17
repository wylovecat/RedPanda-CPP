#!/usr/bin/env bash
# 一键构建带「爱思创」功能的 RedPandaIDE Windows 安装包。
#
# 运行环境：Windows 上的 MSYS2，用开始菜单里的「MSYS2 MSYS」打开 shell 后执行：
#   bash /d/aisichung/build-windows-installer.sh
# 不要在 CMD、PowerShell 或 Git Bash 里跑（脚本第一步会挡住）。
#
# 可选环境变量：
#   BRANCH    要构建的分支（默认 feature/aisichuang）
#   PROFILE   MinGW Lite 配置（默认 64-ucrt，即 64 位 + UCRT 运行库）
#   WORK_DIR  源码与构建目录（默认 ~/rpbuild）
#   https_proxy  走代理时填，例如 http://127.0.0.1:7890
set -euo pipefail

REPO_URL="${REPO_URL:-https://github.com/wylovecat/RedPanda-CPP.git}"
BRANCH="${BRANCH:-feature/aisichuang}"
PROFILE="${PROFILE:-64-ucrt}"
WORK_DIR="${WORK_DIR:-$HOME/rpbuild}"
OUT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [[ -z "${MSYSTEM:-}" ]]; then
  echo "错误：请在开始菜单打开「MSYS2 MSYS」终端后再运行本脚本。" >&2
  echo "（脚本依赖 /mingw64/bin/cmake、/mingw32/bin/makensis 这些 MSYS2 里的路径）" >&2
  exit 1
fi

if [[ -n "${https_proxy:-}" ]]; then
  git config --global http.proxy "${https_proxy}" || true
  echo "已给 git 设置代理：${https_proxy}"
fi

echo "==> 安装基础工具（cmake/ninja/nsis 由 build-xp.sh 自己装）"
pacman -S --noconfirm --needed git tar zstd curl unzip

echo "==> 取源码：$BRANCH"
mkdir -p "$WORK_DIR"
if [[ -d "$WORK_DIR/RedPanda-CPP/.git" ]]; then
  git -C "$WORK_DIR/RedPanda-CPP" fetch origin "$BRANCH"
  git -C "$WORK_DIR/RedPanda-CPP" checkout -B "$BRANCH" FETCH_HEAD
  # 保留已下载的 assets/（Qt、工具链）与上次产物 dist/
  git -C "$WORK_DIR/RedPanda-CPP" clean -fd -e assets -e dist
else
  # 全量克隆：CMake 会用 git rev-list 计数生成版本号，浅克隆会让版本数字不准
  git clone -b "$BRANCH" "$REPO_URL" "$WORK_DIR/RedPanda-CPP"
fi

echo "==> 构建 + NSIS 打包（约 20-40 分钟，期间会下载 Qt 等依赖，约 3-8 GB）"
cd "$WORK_DIR/RedPanda-CPP"
build_args=(-p "$PROFILE" --ucrt)
if [[ "${WITH_COMPILER:-0}" == "1" ]]; then
  # 打包内置 MinGW64 编译器：目标机子上没有编译器时用这个（多下载约 1 GB）
  build_args+=(--mingw64)
fi
bash packages/mingw/build-xp.sh "${build_args[@]}"

echo "==> 拷贝产物到 $OUT_DIR"
cp -v dist/*.exe dist/*.7z "$OUT_DIR"/ 2>/dev/null || {
  echo "警告：dist/ 里没找到 exe/7z，请检查上面的构建日志。" >&2
  exit 1
}

echo
echo "完成。安装包形如 redpanda-cpp-<版本>-ws2003_x64-mingw64.exe，"
echo "另有同名 .7z 免安装便携版。两个文件都会被坚果云同步上去。"

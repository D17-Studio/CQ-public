#!/bin/bash
set -euo pipefail
# ============================================================
# sync-public.sh
# 从私有库同步代码到公开库，排除美术/音频/闭源SDK。
# 每次在私有库开发完成后运行此脚本即可。
# ============================================================

# ---- 路径配置（按需修改） ----
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PRIVATE_REPO="$(cd "$SCRIPT_DIR" && git rev-parse --show-toplevel)"
PUBLIC_REPO="${PUBLIC_REPO:-$PRIVATE_REPO/../CQ-public}"

# ---- 排除清单 ----
# 贴图/纹理
IMG_EXTS=("png" "jpg" "jpeg" "psd" "tga" "tif" "tiff" "bmp" "gif" "exr" "hdr" "iff" "ico")
# 音频
AUDIO_EXTS=("wav" "mp3" "ogg" "aif" "aiff" "flac" "m4a" "wma" "xm" "mod" "it" "s3m" "acb" "awb" "acf" "bank" "bytes")
# 3D模型
MODEL_EXTS=("fbx" "obj" "blend" "3ds" "dae" "max" "ma" "mb" "usd" "usdz")
# 字体
FONT_EXTS=("ttf" "otf" "ttc" "dfont")
# 视频
VIDEO_EXTS=("mp4" "mov" "avi" "webm" "mkv" "wmv")
# 二进制
BIN_EXTS=("dll" "pdb" "exe" "br" "fspro")
# 全部资产扩展名
ALL_ASSET_EXTS=("${IMG_EXTS[@]}" "${AUDIO_EXTS[@]}" "${MODEL_EXTS[@]}" "${FONT_EXTS[@]}" "${VIDEO_EXTS[@]}" "${BIN_EXTS[@]}")

# ---- 排除的目录（相对于 Assets/ 或 _Project/ 的根） ----
EXCLUDED_DIRS=(
    "Assets/ThirdParty/CRIMW"
    "Assets/ThirdParty/FMOD"
    "Assets/TextMesh Pro"
    "_Project/FMOD_Projects"
)

# ---- 带颜色的输出 ----
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'
info()  { echo -e "${GREEN}[INFO]${NC}  $*"; }
warn()  { echo -e "${YELLOW}[WARN]${NC}  $*"; }
err()   { echo -e "${RED}[ERR]${NC}   $*"; }

# ---- 检查前置条件 ----
if [ ! -d "$PRIVATE_REPO" ]; then
    err "私有库路径不存在: $PRIVATE_REPO"
    echo "  用法: PRIVATE_REPO=/path/to/private ./sync-public.sh"
    exit 1
fi

if [ ! -d "$PRIVATE_REPO/Assets" ]; then
    err "私有库中找不到 Assets 目录，请确认路径正确: $PRIVATE_REPO"
    exit 1
fi

# ---- 确认操作 ----
echo ""
echo "  私有库: $PRIVATE_REPO"
echo "  公开库: $PUBLIC_REPO"
echo ""
echo "  将执行以下操作:"
echo "  1. 清空公开库的 Assets/ 和 _Project/"
echo "  2. 从私有库复制代码文件"
echo "  3. 删除美术/音频/闭源SDK文件"
echo ""

read -p "  确认同步? (y/N) " -r CONFIRM
if [ "$CONFIRM" != "y" ] && [ "$CONFIRM" != "Y" ]; then
    info "已取消"
    exit 0
fi

PRIVATE_COMMIT=$(git -C "$PRIVATE_REPO" rev-parse --short=7 HEAD 2>/dev/null || echo "unknown")
info "私有库 commit: $PRIVATE_COMMIT"

# ---- 步骤1：清空目标目录 ----
info "清空公开库 Assets/ 和 _Project/ ..."
rm -rf "$PUBLIC_REPO/Assets" "$PUBLIC_REPO/_Project"

# ---- 步骤2：全量复制 ----
info "复制 Assets/ ..."
cp -r "$PRIVATE_REPO/Assets" "$PUBLIC_REPO/Assets"

if [ -d "$PRIVATE_REPO/_Project" ]; then
    info "复制 _Project/ ..."
    cp -r "$PRIVATE_REPO/_Project" "$PUBLIC_REPO/_Project"
fi

# ---- 步骤3：删除不应公开的文件 ----
info "删除资产文件（贴图、音频、模型、字体、视频、二进制）..."

deleted_count=0

# 按扩展名删除
for ext in "${ALL_ASSET_EXTS[@]}"; do
    while IFS= read -r -d '' file; do
        rm -f "$file"
        ((deleted_count++)) || true
    done < <(find "$PUBLIC_REPO/Assets" "$PUBLIC_REPO/_Project" -type f -name "*.$ext" -print0 2>/dev/null || true)
done

# 删除闭源SDK和FMOD工程目录（整个目录删除）
for dir in "${EXCLUDED_DIRS[@]}"; do
    target="$PUBLIC_REPO/$dir"
    if [ -d "$target" ]; then
        # 统计目录内文件数
        count=$(find "$target" -type f 2>/dev/null | wc -l)
        rm -rf "$target"
        deleted_count=$((deleted_count + count))
        info "  已删除目录: $dir ($count 个文件)"
    fi
done

info "共删除 $deleted_count 个不应公开的文件"

# ---- 步骤4：Git 提交 ----
cd "$PUBLIC_REPO"

info "Git 状态概览:"
echo ""
git status --short | head -20
echo ""
echo "  $(git ls-files -o --exclude-standard | wc -l) 个新文件待添加"
echo "  $(git diff --name-only | wc -l) 个文件已修改"
echo "  $(git diff --staged --name-only | wc -l) 个文件已暂存"
echo ""

read -p "  提交这些变更? (y/N) " -r COMMIT_CONFIRM
if [ "$COMMIT_CONFIRM" != "y" ] && [ "$COMMIT_CONFIRM" != "Y" ]; then
    info "跳过提交，文件已就绪，请手动 git add && git commit"
    exit 0
fi

git add -A

if git diff --staged --quiet; then
    info "没有变更需要提交"
else
    git commit -m "Sync from private@${PRIVATE_COMMIT}

Co-Authored-By: Claude Code <noreply@anthropic.com>"
    info "已提交"
    echo ""
    echo "  下一步: git push origin main"
fi

#!/bin/bash
# Demo deploy script for Menuki plugin-demo example

ENV="production"
while [[ $# -gt 0 ]]; do
    case "$1" in
        --env) ENV="$2"; shift 2 ;;
        *) shift ;;
    esac
done

echo "=============================="
echo "  Deploy to: $ENV"
echo "=============================="
echo ""
echo "[1/4] Pulling latest changes..."
sleep 1
echo "  ✓ git pull origin main"
echo ""
echo "[2/4] Installing dependencies..."
sleep 1
echo "  ✓ dependencies installed"
echo ""
echo "[3/4] Running migrations..."
sleep 1
echo "  ✓ database up to date"
echo ""
echo "[4/4] Restarting services..."
sleep 1
echo "  ✓ services restarted"
echo ""
echo "=============================="
echo "  Deploy to $ENV completed!"
echo "=============================="

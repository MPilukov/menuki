#!/bin/bash
# Demo backup script for Menuki plugin-demo example

TIMESTAMP=$(date +%Y%m%d_%H%M%S)

echo "=============================="
echo "  Backup started: $TIMESTAMP"
echo "=============================="
echo ""
echo "[1/3] Dumping database..."
sleep 1
echo "  ✓ db_backup_$TIMESTAMP.sql"
echo ""
echo "[2/3] Archiving files..."
sleep 1
echo "  ✓ files_backup_$TIMESTAMP.tar.gz"
echo ""
echo "[3/3] Uploading to storage..."
sleep 1
echo "  ✓ uploaded to s3://backups/"
echo ""
echo "=============================="
echo "  Backup completed!"
echo "=============================="

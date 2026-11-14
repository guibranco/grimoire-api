#!/bin/bash
# One-time setup on vinhedo1 so GitHub Actions can deploy Grimoire API.
#
# Creates a restricted SSH user whose key can do exactly one thing:
# stream a release tarball into /usr/local/bin/grimoire-deploy (via sudo).
# Mirrors the existing restricted-user pattern on the server.
#
# The user name and public host name are arguments on purpose: they are
# deployment secrets (GitHub secrets SSH_USER / SSH_HOST) and must not be
# committed to this public repository.
#
# Usage (from the repo root, on your machine):
#   ssh <server> mkdir -p /tmp/grimoire-setup
#   scp deploy/vinhedo1/* deploy_key.pub <server>:/tmp/grimoire-setup/
#   ssh <server> 'sudo bash /tmp/grimoire-setup/setup-server.sh <deploy-user> <public-host> /tmp/grimoire-setup/deploy_key.pub'
#
# Idempotent: safe to re-run (e.g. to rotate the deploy key).
set -euo pipefail

USAGE="usage: setup-server.sh <deploy-user> <public-host> <deploy_key.pub>"
USER_NAME="${1:?$USAGE}"
PUBLIC_HOST="${2:?$USAGE}"
PUBKEY_FILE="${3:?$USAGE}"
SRC_DIR="$(cd "$(dirname "$0")" && pwd)"

[ "$(id -u)" -eq 0 ] || { echo "run as root (sudo)" >&2; exit 1; }
[[ "$USER_NAME" =~ ^[a-z_][a-z0-9_-]{0,31}$ ]] || { echo "invalid user name: $USER_NAME" >&2; exit 1; }
[[ "$PUBLIC_HOST" =~ ^[A-Za-z0-9.-]+$ ]] || { echo "invalid host: $PUBLIC_HOST" >&2; exit 1; }
grep -Eq '^ssh-ed25519 [A-Za-z0-9+/=]+( .*)?$' "$PUBKEY_FILE" || { echo "not an ed25519 public key: $PUBKEY_FILE" >&2; exit 1; }

echo "→ Installing deploy scripts"
for f in grimoire-deploy grimoire-deploy-ssh; do
  sed 's/\r$//' "$SRC_DIR/$f" | install -o root -g root -m 755 /dev/stdin "/usr/local/bin/$f"
  bash -n "/usr/local/bin/$f"
done

echo "→ Creating user $USER_NAME"
id "$USER_NAME" >/dev/null 2>&1 || useradd --create-home --shell /bin/bash "$USER_NAME"
passwd -l "$USER_NAME" >/dev/null
install -d -o "$USER_NAME" -g "$USER_NAME" -m 700 "/home/$USER_NAME/.ssh"
printf 'restrict %s\n' "$(tr -d '\r' < "$PUBKEY_FILE")" \
  | install -o "$USER_NAME" -g "$USER_NAME" -m 600 /dev/stdin "/home/$USER_NAME/.ssh/authorized_keys"

echo "→ sudoers: only /usr/local/bin/grimoire-deploy"
SUDOERS_TMP=$(mktemp)
echo "$USER_NAME ALL=(root) NOPASSWD: /usr/local/bin/grimoire-deploy" > "$SUDOERS_TMP"
visudo -cf "$SUDOERS_TMP"
install -o root -g root -m 440 "$SUDOERS_TMP" /etc/sudoers.d/grimoire-deploy
rm -f "$SUDOERS_TMP"

echo "→ sshd: ForceCommand + AllowUsers"
cat > /etc/ssh/sshd_config.d/90-grimoire-deploy.conf <<EOF
Match User $USER_NAME
    ForceCommand /usr/local/bin/grimoire-deploy-ssh
    PermitTTY no
    AllowTcpForwarding no
    AllowAgentForwarding no
    X11Forwarding no
EOF
HARDENING=/etc/ssh/sshd_config.d/99-hardening.conf
if [ -f "$HARDENING" ] && grep -q '^AllowUsers ' "$HARDENING" && ! grep -Eq "^AllowUsers .*\b$USER_NAME\b" "$HARDENING"; then
  sed -i "s/^AllowUsers .*/& $USER_NAME/" "$HARDENING"
fi
sshd -t
systemctl reload ssh

echo
echo "✅ Done. Value for the GitHub secret SSH_KNOWN_HOSTS:"
echo "$PUBLIC_HOST $(cut -d' ' -f1,2 /etc/ssh/ssh_host_ed25519_key.pub)"

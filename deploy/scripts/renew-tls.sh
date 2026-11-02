#!/usr/bin/env bash
source "$(dirname "$0")/common.sh"
lock_deploy
dc run --rm --no-deps certbot renew --webroot -w /var/www/acme --non-interactive
dc exec -T nginx nginx -t
dc exec -T nginx nginx -s reload

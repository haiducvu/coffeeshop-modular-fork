#!/bin/sh

set -eu

[ "${EXPECT_BARISTA_WORKER_TELEMETRY:-false}" = true ]
[ "${EXPECT_KITCHEN_WORKER_TELEMETRY:-false}" = true ]
printf '%s\n' phase-3 >> "${FAKE_PHASE4_TRACE:?}"

#!/bin/sh

set -u

script_directory="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
repository_root="$(CDPATH= cd -- "${script_directory}/../.." && pwd)"
fake_path="${script_directory}/fakes"
smoke_script="${repository_root}/scripts/phase-2-identity-smoke.sh"
failures=0

run_smoke() {
  response="$1"
  if output="$(PATH="${fake_path}:$PATH" \
    FAKE_IDENTITY_RESPONSE="$response" \
    API_URL="http://api.test" \
    KEYCLOAK_URL="http://identity.test" \
    SMOKE_TIMEOUT_SECONDS=5 \
    "$smoke_script" 2>&1)"; then
    status=0
  else
    status=$?
  fi
}

assert_rejected() {
  name="$1"
  response="$2"
  run_smoke "$response"

  if [ "$status" -eq 0 ]; then
    echo "FAIL: ${name} subject was accepted." >&2
    return 1
  fi
  case "$output" in
    *"The diagnostic endpoint did not return an authenticated subject."*) ;;
    *)
      echo "FAIL: ${name} subject failed for the wrong reason." >&2
      return 1
      ;;
  esac
  case "$output" in
    *"$response"*)
      echo "FAIL: ${name} response was printed by the smoke script." >&2
      return 1
      ;;
  esac

  echo "PASS: ${name} subject was rejected without printing the response."
}

assert_accepted() {
  response="$1"
  run_smoke "$response"

  if [ "$status" -ne 0 ]; then
    echo "FAIL: meaningful subject was rejected." >&2
    return 1
  fi
  case "$output" in
    *"Phase 2 identity smoke test passed"*) ;;
    *)
      echo "FAIL: meaningful subject did not reach the success outcome." >&2
      return 1
      ;;
  esac

  echo "PASS: meaningful subject was accepted."
}

assert_rejected "missing" '{"scopes":[]}' || failures=$((failures + 1))
assert_rejected "non-string" '{"subject":17}' || failures=$((failures + 1))
assert_rejected "empty" '{"subject":""}' || failures=$((failures + 1))
assert_rejected "whitespace-only" '{"subject":" \t\n"}' || failures=$((failures + 1))
assert_accepted '{"subject":"lesson-17-user"}' || failures=$((failures + 1))

if [ "$failures" -ne 0 ]; then
  echo "Identity smoke behavior tests failed: ${failures} case(s)." >&2
  exit 1
fi

echo "Identity smoke behavior tests passed."

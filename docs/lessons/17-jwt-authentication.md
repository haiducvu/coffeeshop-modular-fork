# Bài 17: Xác thực API client bằng JWT Bearer

## Mục tiêu

Host ASP.NET Core có thể xác thực access token JWT từ một OpenID Connect authority.
Authentication là host concern: Domain, SharedKernel, Contracts và các module không biết
ASP.NET Core, JWT hay Keycloak. Authorization policy và role enforcement cho order API
thuộc bài 18; bài này chỉ bảo vệ endpoint diagnostic `/v2/authentication` để chứng minh
principal thật đã đi qua middleware.

## Cấu hình

Section `Authentication` có bốn giá trị:

```json
{
  "Enabled": true,
  "Authority": "https://identity.example/realms/coffeeshop",
  "Audience": "coffeeshop-api",
  "RequireHttpsMetadata": true
}
```

Khi `Enabled=true`, host validate authority/issuer, audience, signature và lifetime,
với clock skew bằng zero. Authority phải là HTTP(S) absolute URI và audience không được
rỗng; options fail khi host start. `RequireHttpsMetadata=false` chỉ dành cho Keycloak
`start-dev` local. Production phải dùng HTTPS và giữ giá trị mặc định `true`.

Khi `Enabled=false`, host không gọi `AddAuthentication`, không đăng ký scheme và không
tạo principal giả. `/v1`, `/message`, DataGen và endpoint health giữ nguyên Phase 1.

## Test không phụ thuộc IdP

Functional tests thay default scheme bằng `TestAuthenticationHandler`; handler chỉ phát
ticket khi request gửi explicit header `Authorization: Test deterministic-ticket`. Nó
không bỏ qua endpoint authorization, nên chính `RequireAuthorization` vẫn quyết định
anonymous request là `401`.

Một test host khác giữ nguyên JWT bearer handler, inject signing key và OpenID metadata
tĩnh, rồi gửi JWT thật. Fixtures có thời gian cố định xa trong quá khứ/tương lai nên
không dùng clock hay network. Test chứng minh token hợp lệ được nhận, còn expired token,
signature sai, issuer sai và audience sai đều bị từ chối.

## Keycloak local và issuer boundary

Compose profile `identity` dùng `quay.io/keycloak/keycloak:26.5.2`, chạy
`start-dev --import-realm`, mount `deploy/keycloak/coffeeshop-realm.json` read-only vào
`/opt/keycloak/data/import`, và bind `127.0.0.1:18080`. API container dùng authority
`http://keycloak:8080/realms/coffeeshop`; curl trên host dùng
`http://localhost:18080`. Keycloak cố định public issuer ở URL loopback và bật dynamic
backchannel để discovery qua Docker service name quảng bá JWKS nội bộ có thể truy cập.
Identity smoke thực tế là gate phát hiện nếu hai network location không tương thích về
discovery, JWKS hoặc issuer.

Realm có public direct-grant client `coffeeshop-api` và user local
`lesson17-user` / `lesson17-local`. Đây là credentials không bí mật chỉ dùng cho local
learning/smoke. Password grant và `start-dev` không phải production architecture.

## Chạy bài học

```bash
dotnet restore CoffeeShop.slnx
dotnet test tests/CoffeeShop.ApiTests/CoffeeShop.ApiTests.csproj --configuration Debug --filter FullyQualifiedName~AuthenticationTests
dotnet build CoffeeShop.slnx --configuration Release --no-restore
dotnet test CoffeeShop.slnx --configuration Release --no-build

docker compose --profile demo --profile identity build
docker compose down --volumes --remove-orphans
docker compose up -d postgres api signalr-client
./scripts/phase-1-smoke.sh
docker compose down --volumes --remove-orphans

AUTHENTICATION_ENABLED=true docker compose --profile identity up -d postgres keycloak api
./scripts/phase-2-identity-smoke.sh
docker compose --profile identity down --volumes --remove-orphans
```

Identity smoke có một global deadline: chờ discovery, lấy token từ token endpoint, rồi
gọi endpoint authenticated. Script cần Docker Compose, `curl` và `jq`; CI kiểm tra
prerequisite và chạy controlled behavior harness trước Keycloak. Khi fail, script in
trạng thái và logs của Keycloak/API nhưng không in token, password hay identity body.

## Kiến thức cần nhớ

- Authentication xác minh ai gọi; authorization quyết định người đó được làm gì.
- `Authority` không thay thế audience validation; API phải kiểm tra cả issuer, audience,
  signature và lifetime.
- Test scheme phải thay authentication deterministically, không thay/bypass authorization.
- Local IdP smoke bổ sung cho fixture tests vì nó kiểm tra discovery, JWKS, issuer và
  container networking thật.

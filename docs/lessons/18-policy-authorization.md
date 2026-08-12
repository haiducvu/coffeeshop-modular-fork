# Bài 18: Phân quyền thao tác bằng policy

## Mục tiêu

Bài này thêm authorization tại API host, sau khi bài 17 đã xác thực JWT Bearer.
Counter, Domain, Contracts và SharedKernel không biết role, HTTP principal hay Keycloak.

`CoffeeShopPolicies` là nơi duy nhất giữ policy name và realm role string:

- `CoffeeShop.Customer` / `customer`: tạo đơn qua `POST /v2/orders`.
- `CoffeeShop.FulfillmentReader` / `fulfillment-reader`: đọc `GET /v2/fulfillment-orders`.
  Role `operator` cũng được policy này cho phép.
- `CoffeeShop.Operator` / `operator`: đọc `GET /v2/operations/orders/{id}`.
- `CoffeeShop.OrderOwner`: resource policy cho đơn khách hàng.

`GET /v2/orders/{id}` trước hết đòi hỏi principal đã xác thực. Sau khi host tải
`OrderDetails`, `IAuthorizationService.AuthorizeAsync` gửi resource đó cho
`OrderOwnerAuthorizationHandler`. Handler chỉ cho phép customer có `sub` GUID bằng
`LoyaltyMemberId` của đơn; operator được override. Role check không nằm trong Counter.

## Realm role và ownership

Keycloak đặt realm role trong `realm_access.roles`. JWT bearer callback map các role này
một lần sang `ClaimTypes.Role` lúc token được validate, nên mọi named policy dùng
`RequireRole` bình thường. `MapInboundClaims=false` vẫn được giữ để `sub` là claim ổn
định dùng cho ownership.

Realm demo có ba identity local cố định. Identity customer dùng cùng GUID với
`LoyaltyMemberId` smoke để minh họa seam ownership. Đây chỉ là fixture học tập; hệ thống
production phải có mapping subject-to-loyalty-member ổn định do identity provider quản lý,
không nhận ownership từ request body.

## Compatibility và fail-closed

`/v1`, SignalR, health và DataGen không đổi, tiếp tục anonymous cho compatibility.
Khi `Authentication:Enabled=false`, host không map bất kỳ route `/v2` nào: request nhận
`404`, thay vì tạo principal giả hoặc lộ một route bảo vệ yếu. Muốn dùng `/v2` phải bật
authentication và cấu hình authority/audience hợp lệ.

## Chạy bài học

```bash
dotnet restore CoffeeShop.slnx
dotnet test tests/CoffeeShop.ApiTests/CoffeeShop.ApiTests.csproj --configuration Debug --filter FullyQualifiedName~AuthorizationMatrixTests
./tests/scripts/phase-2-identity-smoke-tests.sh

AUTHENTICATION_ENABLED=true docker compose --profile identity up -d --build postgres keycloak api
./scripts/phase-2-identity-smoke.sh
docker compose --profile identity down --volumes --remove-orphans
```

Smoke có một global deadline và lần lượt lấy token customer, fulfillment-reader, operator.
Nó chứng minh customer tạo/đọc đơn của mình, fulfillment reader đọc queue, operator override
ownership và dùng operational route. Script không in token, password hay response identity.

## Kiến thức cần nhớ

- Authentication xác minh principal; policy authorization giới hạn endpoint; resource
  authorization quyết định principal có quyền trên resource đã tải hay không.
- Named policy giữ role semantics ở host boundary và tránh role string rải rác.
- Authorization không thay thế ownership: customer role không cho phép đọc đơn của customer
  khác; privileged operator override là một rule rõ ràng, có test.
- Tắt authentication không bao giờ là lý do để mở `/v2`; behavior fail-closed giữ Phase 1
  public riêng với API policy-protected.

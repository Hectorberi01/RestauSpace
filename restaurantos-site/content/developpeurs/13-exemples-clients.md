# Clients prêts à l'emploi

> Un petit client de l'API de vente en ligne à copier dans votre projet : en-têtes, erreurs par `code`, idempotence,
> cache de la carte, session client et jeton de borne. En TypeScript, Flutter (Dart), Swift et Kotlin.

Chaque client fait la même chose :

- ajoute `X-Storefront-Key`, `Accept` et, s'ils existent, `X-Customer-Token` et `X-Kiosk-Device` ;
- transforme une réponse en erreur `StorefrontError(status, code, retryAfter)` ;
- oublie la session client sur `403 storefront.customer.sign_in_required` ;
- garde la carte et son `ETag` et la relit avec `If-None-Match`.

La génération de la clé d'idempotence reste à l'appelant : créez-la au clic sur « Commander » et réutilisez-la pour
les nouveaux essais du même envoi.

## TypeScript (navigateur, React Native, Node)

```ts
export class StorefrontError extends Error {
  constructor(public status: number, public code: string | null, public retryAfter: number | null) {
    super(code ?? `http_${status}`);
  }
}

type Options = {method?: 'GET' | 'POST'; body?: unknown; idempotencyKey?: string; humanCheck?: string};

export class Storefront {
  customerToken: string | null = null;
  deviceToken: string | null = null;
  private catalogs = new Map<string, {etag: string; body: unknown}>();

  constructor(private baseUrl: string, private key: string) {}

  async call<T>(path: string, options: Options = {}): Promise<T> {
    const headers: Record<string, string> = {'X-Storefront-Key': this.key, Accept: 'application/json'};
    if (this.customerToken) headers['X-Customer-Token'] = this.customerToken;
    if (this.deviceToken) headers['X-Kiosk-Device'] = this.deviceToken;
    if (options.idempotencyKey) headers['Idempotency-Key'] = options.idempotencyKey;
    if (options.humanCheck) headers['X-Human-Check'] = options.humanCheck;
    if (options.body !== undefined) headers['Content-Type'] = 'application/json';

    let response: Response;
    try {
      response = await fetch(`${this.baseUrl}/v1/storefront${path}`, {
        method: options.method ?? 'GET',
        headers,
        body: options.body === undefined ? undefined : JSON.stringify(options.body),
      });
    } catch {
      throw new StorefrontError(0, null, null);
    }
    if (response.status === 204) return undefined as T;
    if (response.ok) return (await response.json()) as T;

    const problem = await response.json().catch(() => null);
    const code: string | null = problem?.code ?? null;
    if (code === 'storefront.customer.sign_in_required') this.customerToken = null;
    const retryAfter = Number(response.headers.get('Retry-After')) || null;
    throw new StorefrontError(response.status, code, retryAfter);
  }

  bootstrap() {
    return this.call<StorefrontInfo>('/');
  }

  async catalog(locationId: string) {
    const cached = this.catalogs.get(locationId);
    const headers: Record<string, string> = {'X-Storefront-Key': this.key, Accept: 'application/json'};
    if (this.deviceToken) headers['X-Kiosk-Device'] = this.deviceToken;
    if (cached) headers['If-None-Match'] = cached.etag;
    const response = await fetch(`${this.baseUrl}/v1/storefront/locations/${locationId}/catalog`, {headers});
    if (response.status === 304 && cached) return cached.body as Catalog;
    if (!response.ok) throw new StorefrontError(response.status, (await response.json().catch(() => null))?.code ?? null, null);
    const body = (await response.json()) as Catalog;
    const etag = response.headers.get('ETag');
    if (etag) this.catalogs.set(locationId, {etag, body});
    return body;
  }

  quote(cart: QuoteRequest) {
    return this.call<CartQuote>('/cart/quote', {method: 'POST', body: cart});
  }

  placeOrder(order: PlaceOrderRequest, idempotencyKey: string, humanCheck?: string) {
    return this.call<PlacedOrder>('/orders', {method: 'POST', body: order, idempotencyKey, humanCheck});
  }

  track(trackingToken: string) {
    return this.call<TrackedOrder>(`/orders/${encodeURIComponent(trackingToken)}`);
  }

  pay(trackingToken: string, payment: {provider: string; method?: string; payerPhone?: string; returnUrl?: string | null}, idempotencyKey: string) {
    return this.call<{paymentId: string; checkoutUrl: string | null}>(`/orders/${encodeURIComponent(trackingToken)}/payments`, {method: 'POST', body: payment, idempotencyKey});
  }

  reviews(options: {locationId?: string; take?: number} = {}) {
    const query = new URLSearchParams();
    if (options.locationId) query.set('locationId', options.locationId);
    if (options.take !== undefined) query.set('take', String(options.take));
    return this.call<Reviews>(`/reviews${query.size ? `?${query}` : ''}`);
  }

  leaveReview(trackingToken: string, rating: number, comment?: string) {
    return this.call<{id: string}>('/reviews', {method: 'POST', body: {orderToken: trackingToken, rating, comment: comment ?? null}});
  }

  async signIn(channel: 'Sms' | 'WhatsApp' | 'Email', address: string, askCode: () => Promise<string>, askName: () => Promise<string>) {
    const sent = await this.call<{ticket: string}>('/customer/code', {method: 'POST', body: {channel, address, language: 'fr'}});
    let result = await this.call<SignIn>('/customer/code/verify', {method: 'POST', body: {ticket: sent.ticket, code: await askCode(), language: 'fr'}});
    if (result.status === 'NameRequired') {
      result = await this.call<SignIn>('/customer/code/verify', {method: 'POST', body: {ticket: result.ticket, name: await askName(), language: 'fr'}});
    }
    this.customerToken = result.token;
    return result;
  }
}

export interface StorefrontInfo { restaurant: Restaurant; orderingEnabled: boolean; locations: Location[] }
export interface Restaurant {
  name: string; currencyCode: string; primaryColor: string | null; logoUrl: string | null;
  tagline: string | null; description: string | null; coverImageUrl: string | null; links: {network: string; url: string}[];
}
export interface TimeRange { start: string; end: string; endsNextDay: boolean }
export interface Location {
  id: string; name: string; street: string; city: string; timeZoneId: string;
  isOpenNow: boolean; nextOpeningUtc: string | null; acceptsOrdersNow: boolean; closureReason: string | null;
  serviceModes: ServiceMode[]; preparationMinutes: number;
  weeklyHours: (TimeRange & {day: string})[];
  specialDays: {date: string; isClosed: boolean; ranges: TimeRange[]; reason: string | null}[];
  place: Place | null;
}
export interface Place {
  postalCode: string | null; countryCode: string;
  coordinates: {latitude: number; longitude: number} | null;
  contact: {phone: string | null; whatsApp: string | null; email: string | null};
}
export interface Reviews {
  count: number; averageRating: number | null; countByRating: {rating: number; count: number}[];
  reviews: {id: string; locationId: string; rating: number; comment: string | null; submittedAtUtc: string}[];
}
export type ServiceMode = 'DineIn' | 'Takeaway' | 'Delivery';
export interface Catalog { version: string; currencyCode: string; menus: unknown[]; products: unknown[]; modifierGroups: unknown[] }
export interface CartLine { productId: string; quantity: number; variantId?: string | null; modifierIds?: string[]; note?: string | null }
export interface QuoteRequest { locationId: string; mode: ServiceMode; lines: CartLine[]; couponCode?: string | null; deliveryZoneId?: string | null; rewardId?: string | null }
export interface CartQuote { total: number; currencyCode: string; meetsMinimum: boolean; canPayAtCounter: boolean; canPayOnline: boolean }
export interface PlaceOrderRequest extends QuoteRequest { payment: 'Counter' | 'Online'; expectedTotal: number; customer?: unknown; scheduledForUtc?: string | null; delivery?: unknown; tableToken?: string | null }
export interface PlacedOrder { orderId: string; number: string; status: string; total: number; trackingToken: string; payBeforeUtc: string | null }
export interface TrackedOrder { status: string; paymentState: string; canPay: boolean; canCancel: boolean; estimatedReadyAtUtc: string | null }
export interface SignIn { status: 'SignedIn' | 'NameRequired'; token: string | null; ticket: string | null }
```

Utilisation :

```ts
const storefront = new Storefront('{{API_URL}}', process.env.NEXT_PUBLIC_STOREFRONT_KEY!);
const {locations} = await storefront.bootstrap();
const quote = await storefront.quote({locationId: locations[0].id, mode: 'Takeaway', lines: cart});
const key = crypto.randomUUID();
try {
  const placed = await storefront.placeOrder({locationId: locations[0].id, mode: 'Takeaway', lines: cart, payment: 'Counter', expectedTotal: quote.total, customer}, key);
} catch (error) {
  if (error instanceof StorefrontError && error.code === 'storefront.checkout.price_changed') {
    // refaire le devis et redemander la confirmation
  }
}
```

## Flutter (Dart)

Dépendances : `http`, `uuid`, `flutter_secure_storage`.

```dart
import 'dart:convert';
import 'package:http/http.dart' as http;

class StorefrontException implements Exception {
  StorefrontException(this.status, this.code, this.retryAfter);
  final int status;
  final String? code;
  final int? retryAfter;
  @override
  String toString() => code ?? 'http_$status';
}

class Storefront {
  Storefront(this.baseUrl, this.key, {http.Client? client}) : _http = client ?? http.Client();

  final String baseUrl;
  final String key;
  final http.Client _http;
  String? customerToken;
  String? deviceToken;
  final _catalogs = <String, (String etag, Map<String, dynamic> body)>{};

  Map<String, String> _headers({String? idempotencyKey, String? humanCheck, bool json = false}) => {
        'X-Storefront-Key': key,
        'Accept': 'application/json',
        if (customerToken != null) 'X-Customer-Token': customerToken!,
        if (deviceToken != null) 'X-Kiosk-Device': deviceToken!,
        if (idempotencyKey != null) 'Idempotency-Key': idempotencyKey,
        if (humanCheck != null) 'X-Human-Check': humanCheck,
        if (json) 'Content-Type': 'application/json',
      };

  Future<dynamic> call(String path, {Object? body, String? idempotencyKey, String? humanCheck}) async {
    final uri = Uri.parse('$baseUrl/v1/storefront$path');
    final headers = _headers(idempotencyKey: idempotencyKey, humanCheck: humanCheck, json: body != null);
    final http.Response response;
    try {
      response = body == null ? await _http.get(uri, headers: headers) : await _http.post(uri, headers: headers, body: jsonEncode(body));
    } catch (_) {
      throw StorefrontException(0, null, null);
    }
    if (response.statusCode == 204) return null;
    final decoded = response.bodyBytes.isEmpty ? null : jsonDecode(utf8.decode(response.bodyBytes));
    if (response.statusCode >= 200 && response.statusCode < 300) return decoded;
    final code = decoded is Map<String, dynamic> ? decoded['code'] as String? : null;
    if (code == 'storefront.customer.sign_in_required') customerToken = null;
    throw StorefrontException(response.statusCode, code, int.tryParse(response.headers['retry-after'] ?? ''));
  }

  Future<Map<String, dynamic>> bootstrap() async => await call('/') as Map<String, dynamic>;

  Future<Map<String, dynamic>> catalog(String locationId) async {
    final cached = _catalogs[locationId];
    final response = await _http.get(Uri.parse('$baseUrl/v1/storefront/locations/$locationId/catalog'),
        headers: {..._headers(), if (cached != null) 'If-None-Match': cached.$1});
    if (response.statusCode == 304 && cached != null) return cached.$2;
    if (response.statusCode != 200) throw StorefrontException(response.statusCode, null, null);
    final body = jsonDecode(utf8.decode(response.bodyBytes)) as Map<String, dynamic>;
    final etag = response.headers['etag'];
    if (etag != null) _catalogs[locationId] = (etag, body);
    return body;
  }

  Future<Map<String, dynamic>> quote(Map<String, dynamic> cart) async => await call('/cart/quote', body: cart) as Map<String, dynamic>;

  Future<Map<String, dynamic>> placeOrder(Map<String, dynamic> order, String idempotencyKey, {String? humanCheck}) async =>
      await call('/orders', body: order, idempotencyKey: idempotencyKey, humanCheck: humanCheck) as Map<String, dynamic>;

  Future<Map<String, dynamic>> track(String trackingToken) async =>
      await call('/orders/${Uri.encodeComponent(trackingToken)}') as Map<String, dynamic>;

  Future<Map<String, dynamic>> pay(String trackingToken, Map<String, dynamic> payment, String idempotencyKey) async =>
      await call('/orders/${Uri.encodeComponent(trackingToken)}/payments', body: payment, idempotencyKey: idempotencyKey) as Map<String, dynamic>;

  Future<Map<String, dynamic>> reviews({String? locationId, int take = 10}) async =>
      await call('/reviews?take=$take${locationId == null ? '' : '&locationId=$locationId'}') as Map<String, dynamic>;

  Future<Map<String, dynamic>> leaveReview(String trackingToken, int rating, {String? comment}) async =>
      await call('/reviews', body: {'orderToken': trackingToken, 'rating': rating, 'comment': comment}) as Map<String, dynamic>;
}
```

Utilisation :

```dart
final storefront = Storefront('{{API_URL}}', const String.fromEnvironment('STOREFRONT_KEY'));
storefront.customerToken = await secureStorage.read(key: 'customerToken');
final info = await storefront.bootstrap();
final location = (info['locations'] as List).first as Map<String, dynamic>;
final quote = await storefront.quote({'locationId': location['id'], 'mode': 'Takeaway', 'lines': cart});
final placed = await storefront.placeOrder(
  {'locationId': location['id'], 'mode': 'Takeaway', 'lines': cart, 'payment': 'Counter', 'expectedTotal': quote['total'], 'customer': customer},
  const Uuid().v4(),
);
await secureStorage.write(key: 'order:${placed['orderId']}', value: placed['trackingToken'] as String);
```

## Swift (iOS)

```swift
import Foundation

struct StorefrontError: Error {
    let status: Int
    let code: String?
    let retryAfter: Int?
}

final class Storefront {
    private let baseURL: URL
    private let key: String
    private let session: URLSession
    var customerToken: String?
    var deviceToken: String?
    private var catalogs: [String: (etag: String, data: Data)] = [:]

    init(baseURL: URL, key: String, session: URLSession = .shared) {
        self.baseURL = baseURL
        self.key = key
        self.session = session
    }

    private func makeRequest(_ path: String, method: String = "GET", body: Encodable? = nil, idempotencyKey: String? = nil, humanCheck: String? = nil) throws -> URLRequest {
        var request = URLRequest(url: URL(string: baseURL.absoluteString + "/v1/storefront" + path)!)
        request.httpMethod = method
        request.setValue(key, forHTTPHeaderField: "X-Storefront-Key")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        if let customerToken { request.setValue(customerToken, forHTTPHeaderField: "X-Customer-Token") }
        if let deviceToken { request.setValue(deviceToken, forHTTPHeaderField: "X-Kiosk-Device") }
        if let idempotencyKey { request.setValue(idempotencyKey, forHTTPHeaderField: "Idempotency-Key") }
        if let humanCheck { request.setValue(humanCheck, forHTTPHeaderField: "X-Human-Check") }
        if let body {
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            request.httpBody = try JSONEncoder().encode(body)
        }
        return request
    }

    private func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        let result: (Data, URLResponse)
        do {
            result = try await session.data(for: request)
        } catch {
            throw StorefrontError(status: 0, code: nil, retryAfter: nil)
        }
        let (data, response) = result
        guard let http = response as? HTTPURLResponse else { throw StorefrontError(status: 0, code: nil, retryAfter: nil) }
        if (200..<300).contains(http.statusCode) || http.statusCode == 304 { return (data, http) }
        let code = (try? JSONSerialization.jsonObject(with: data) as? [String: Any])?["code"] as? String
        if code == "storefront.customer.sign_in_required" { customerToken = nil }
        throw StorefrontError(status: http.statusCode, code: code, retryAfter: Int(http.value(forHTTPHeaderField: "Retry-After") ?? ""))
    }

    func call<T: Decodable>(_ path: String, method: String = "GET", body: Encodable? = nil, idempotencyKey: String? = nil, humanCheck: String? = nil) async throws -> T {
        let (data, _) = try await send(try makeRequest(path, method: method, body: body, idempotencyKey: idempotencyKey, humanCheck: humanCheck))
        return try JSONDecoder().decode(T.self, from: data)
    }

    func catalogData(locationId: String) async throws -> Data {
        var request = try makeRequest("/locations/\(locationId)/catalog")
        if let cached = catalogs[locationId] { request.setValue(cached.etag, forHTTPHeaderField: "If-None-Match") }
        let (data, response) = try await send(request)
        if response.statusCode == 304, let cached = catalogs[locationId] { return cached.data }
        if let etag = response.value(forHTTPHeaderField: "ETag") { catalogs[locationId] = (etag, data) }
        return data
    }
}

struct CartLine: Codable { let productId: String; let quantity: Int; var variantId: String? = nil; var modifierIds: [String] = []; var note: String? = nil }
struct QuoteRequest: Encodable { let locationId: String; let mode: String; let lines: [CartLine]; var couponCode: String? = nil }
struct CartQuote: Decodable { let total: Decimal; let currencyCode: String; let meetsMinimum: Bool; let canPayAtCounter: Bool; let canPayOnline: Bool }
struct Customer: Encodable { let name: String; let phone: String?; let email: String?; var marketingConsent = false; var language = "fr" }
struct PlaceOrder: Encodable { let locationId: String; let mode: String; let lines: [CartLine]; let payment: String; let expectedTotal: Decimal; let customer: Customer? }
struct PlacedOrder: Decodable { let orderId: String; let number: String; let status: String; let total: Decimal; let trackingToken: String }
struct Review: Decodable { let id: String; let locationId: String; let rating: Int; let comment: String?; let submittedAtUtc: String }
struct Reviews: Decodable { let count: Int; let averageRating: Decimal?; let reviews: [Review] }
struct LeaveReview: Encodable { let orderToken: String; let rating: Int; let comment: String? }
struct Created: Decodable { let id: String }

extension Storefront {
    func reviews(locationId: String? = nil, take: Int = 10) async throws -> Reviews {
        try await call("/reviews?take=\(take)" + (locationId.map { "&locationId=\($0)" } ?? ""))
    }

    func leaveReview(trackingToken: String, rating: Int, comment: String? = nil) async throws -> Created {
        try await call("/reviews", method: "POST", body: LeaveReview(orderToken: trackingToken, rating: rating, comment: comment))
    }
}
```

Utilisation :

```swift
let storefront = Storefront(baseURL: URL(string: "{{API_URL}}")!, key: Bundle.main.object(forInfoDictionaryKey: "StorefrontKey") as! String)
let quote: CartQuote = try await storefront.call("/cart/quote", method: "POST", body: QuoteRequest(locationId: locationId, mode: "Takeaway", lines: cart))
let placed: PlacedOrder = try await storefront.call(
    "/orders", method: "POST",
    body: PlaceOrder(locationId: locationId, mode: "Takeaway", lines: cart, payment: "Counter", expectedTotal: quote.total,
                     customer: Customer(name: "Afi", phone: "+2290197000000", email: nil)),
    idempotencyKey: orderAttemptKey) // UUID().uuidString, créé au clic sur « Commander »
```

## Kotlin (Android)

Dépendances : `com.squareup.okhttp3:okhttp`, `org.json` (inclus dans Android). Appelez ces fonctions hors du thread
principal (coroutine `Dispatchers.IO`).

```kotlin
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject

class StorefrontException(val status: Int, val code: String?, val retryAfter: Int?) : Exception(code ?: "http_$status")

class Storefront(private val baseUrl: String, private val key: String, private val http: OkHttpClient = OkHttpClient()) {
    @Volatile var customerToken: String? = null
    @Volatile var deviceToken: String? = null
    private val catalogs = mutableMapOf<String, Pair<String, JSONObject>>()

    private fun builder(path: String, idempotencyKey: String? = null, humanCheck: String? = null) =
        Request.Builder().url("$baseUrl/v1/storefront$path")
            .header("X-Storefront-Key", key)
            .header("Accept", "application/json")
            .apply {
                customerToken?.let { header("X-Customer-Token", it) }
                deviceToken?.let { header("X-Kiosk-Device", it) }
                idempotencyKey?.let { header("Idempotency-Key", it) }
                humanCheck?.let { header("X-Human-Check", it) }
            }

    fun call(path: String, body: JSONObject? = null, idempotencyKey: String? = null, humanCheck: String? = null): JSONObject? {
        val request = builder(path, idempotencyKey, humanCheck)
            .apply { if (body != null) post(body.toString().toRequestBody("application/json".toMediaType())) }
            .build()
        val response = try {
            http.newCall(request).execute()
        } catch (e: java.io.IOException) {
            throw StorefrontException(0, null, null)
        }
        response.use {
            val text = it.body?.string().orEmpty()
            if (it.code == 204) return null
            if (it.isSuccessful) return JSONObject(text)
            val code = text.takeIf(String::isNotEmpty)?.let { json -> runCatching { JSONObject(json).optString("code").ifEmpty { null } }.getOrNull() }
            if (code == "storefront.customer.sign_in_required") customerToken = null
            throw StorefrontException(it.code, code, it.header("Retry-After")?.toIntOrNull())
        }
    }

    fun bootstrap(): JSONObject = call("/")!!

    fun catalog(locationId: String): JSONObject {
        val cached = synchronized(catalogs) { catalogs[locationId] }
        val request = builder("/locations/$locationId/catalog").apply { cached?.let { header("If-None-Match", it.first) } }.build()
        http.newCall(request).execute().use {
            if (it.code == 304 && cached != null) return cached.second
            if (!it.isSuccessful) throw StorefrontException(it.code, null, null)
            val body = JSONObject(it.body!!.string())
            it.header("ETag")?.let { etag -> synchronized(catalogs) { catalogs[locationId] = etag to body } }
            return body
        }
    }

    fun quote(cart: JSONObject): JSONObject = call("/cart/quote", cart)!!

    fun placeOrder(order: JSONObject, idempotencyKey: String, humanCheck: String? = null): JSONObject =
        call("/orders", order, idempotencyKey, humanCheck)!!

    fun track(trackingToken: String): JSONObject = call("/orders/${java.net.URLEncoder.encode(trackingToken, "UTF-8")}")!!

    fun pay(trackingToken: String, payment: JSONObject, idempotencyKey: String): JSONObject =
        call("/orders/${java.net.URLEncoder.encode(trackingToken, "UTF-8")}/payments", payment, idempotencyKey)!!

    fun reviews(locationId: String? = null, take: Int = 10): JSONObject =
        call("/reviews?take=$take" + (locationId?.let { "&locationId=$it" } ?: ""))!!

    fun leaveReview(trackingToken: String, rating: Int, comment: String? = null): JSONObject =
        call("/reviews", JSONObject().put("orderToken", trackingToken).put("rating", rating).put("comment", comment ?: JSONObject.NULL))!!
}
```

Utilisation :

```kotlin
val storefront = Storefront("{{API_URL}}", BuildConfig.STOREFRONT_KEY)
storefront.customerToken = securePrefs.getString("customerToken", null)
val location = storefront.bootstrap().getJSONArray("locations").getJSONObject(0)
val quote = storefront.quote(JSONObject().put("locationId", location.getString("id")).put("mode", "Takeaway").put("lines", cart))
val placed = storefront.placeOrder(
    JSONObject()
        .put("locationId", location.getString("id")).put("mode", "Takeaway").put("lines", cart)
        .put("payment", "Counter").put("expectedTotal", quote.get("total"))
        .put("customer", JSONObject().put("name", "Afi").put("phone", "+2290197000000").put("language", "fr")),
    orderAttemptKey, // UUID.randomUUID().toString(), créé au clic sur « Commander »
)
securePrefs.edit().putString("order:${placed.getString("orderId")}", placed.getString("trackingToken")).apply()
```

## Description OpenAPI

Pour générer un client typé (openapi-generator, NSwag, swift-openapi-generator, openapi_generator pour Dart) :

```bash
curl -s {{API_URL}}/openapi/storefront.json -o storefront.json
npx @openapitools/openapi-generator-cli generate -i storefront.json -g typescript-fetch -o src/storefront
```

Les erreurs ne figurent pas dans la description : gérez-les avec le `code` (voir [Codes d'erreur](12-reference-erreurs.md)).

# OpenAI 서비스 API 사용 가이드

이 서비스는 Ollama에 대한 인증된 프록시로, `/api/chat` 및 `/api/generate` 엔드포인트를 외부에 노출합니다.  
실제 요청과 응답은 Ollama에 그대로 전달되므로, [Ollama API 공식 문서](https://github.com/ollama/ollama/blob/main/docs/api.md)와 동일한 요청/응답 형식을 사용할 수 있습니다.

---

## 인증 (Authentication)

모든 API 요청에는 관리자 인증이 필요합니다. 다음 두 가지 방법 중 하나를 사용하세요.

### 방법 1: Bearer 토큰 (access_token)

OAuth2 서비스에서 발급받은 `access_token`을 `Authorization` 헤더에 포함합니다.

```http
Authorization: Bearer <access_token>
```

### 방법 2: Admin API Key

서버의 `appsettings.json`에서 `Ollama:AdminApiKey`로 설정한 값을 Bearer 토큰으로 사용하거나, 쿼리 파라미터로 전달합니다.

```http
Authorization: Bearer <AdminApiKey>
```

또는 쿼리 파라미터:

```
POST /api/chat?access_token=<AdminApiKey>
```

### 인증 실패 시 응답

인증 정보가 없거나 유효하지 않으면 `401 Unauthorized`가 반환됩니다.

```json
{ "error": "unauthorized" }
```

---

## 엔드포인트

### `POST /api/chat`

Ollama의 `/api/chat`에 요청을 그대로 전달합니다. 멀티턴 대화에 사용합니다.

#### 요청 예시 (스트리밍)

```http
POST /api/chat
Authorization: Bearer <token>
Content-Type: application/json

{
  "model": "gemma2:27b",
  "messages": [
    { "role": "user", "content": "안녕하세요! 오늘 날씨 어때요?" }
  ],
  "stream": true
}
```

#### 요청 예시 (단건 응답)

```http
POST /api/chat
Authorization: Bearer <token>
Content-Type: application/json

{
  "model": "gemma2:27b",
  "messages": [
    { "role": "system", "content": "당신은 친절한 AI 어시스턴트입니다." },
    { "role": "user",   "content": "파이썬으로 피보나치 수열을 출력하는 코드를 작성해줘." }
  ],
  "stream": false
}
```

#### 스트리밍 응답 형식 (`stream: true`)

응답이 NDJSON(개행으로 구분된 JSON) 형식으로 청크 단위로 전달됩니다.

```jsonl
{"model":"gemma2:27b","created_at":"...","message":{"role":"assistant","content":"안"},"done":false}
{"model":"gemma2:27b","created_at":"...","message":{"role":"assistant","content":"녕"},"done":false}
...
{"model":"gemma2:27b","created_at":"...","message":{"role":"assistant","content":""},"done":true,"total_duration":...}
```

#### 단건 응답 형식 (`stream: false`)

```json
{
  "model": "gemma2:27b",
  "created_at": "2024-01-01T00:00:00Z",
  "message": {
    "role": "assistant",
    "content": "안녕하세요! 저는 AI 어시스턴트입니다."
  },
  "done": true,
  "total_duration": 1234567890
}
```

---

### `POST /api/generate`

Ollama의 `/api/generate`에 요청을 그대로 전달합니다. 단일 프롬프트에 대한 텍스트 생성에 사용합니다.

#### 요청 예시 (스트리밍)

```http
POST /api/generate
Authorization: Bearer <token>
Content-Type: application/json

{
  "model": "llama3.1:8b",
  "prompt": "하늘은 왜 파란색인가요?",
  "stream": true
}
```

#### 요청 예시 (단건 응답)

```http
POST /api/generate
Authorization: Bearer <token>
Content-Type: application/json

{
  "model": "llama3.1:8b",
  "prompt": "하늘은 왜 파란색인가요?",
  "stream": false
}
```

#### 스트리밍 응답 형식 (`stream: true`)

```jsonl
{"model":"llama3.1:8b","created_at":"...","response":"하","done":false}
{"model":"llama3.1:8b","created_at":"...","response":"늘","done":false}
...
{"model":"llama3.1:8b","created_at":"...","response":"","done":true,"total_duration":...}
```

#### 단건 응답 형식 (`stream: false`)

```json
{
  "model": "llama3.1:8b",
  "created_at": "2024-01-01T00:00:00Z",
  "response": "하늘이 파란 이유는 레일리 산란 때문입니다...",
  "done": true,
  "total_duration": 1234567890
}
```

---

## curl 호출 예시

### API Key를 이용한 `/api/chat` 스트리밍 호출

```bash
curl -X POST https://<서비스_주소>/api/chat \
  -H "Authorization: Bearer MY_ADMIN_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "model": "gemma2:27b",
    "messages": [{"role": "user", "content": "안녕하세요!"}],
    "stream": true
  }'
```

### access_token을 이용한 `/api/generate` 단건 호출

```bash
curl -X POST https://<서비스_주소>/api/generate \
  -H "Authorization: Bearer MY_ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "model": "llama3.1:8b",
    "prompt": "AI란 무엇인가요?",
    "stream": false
  }'
```

### 쿼리 파라미터로 API Key 전달

```bash
curl -X POST "https://<서비스_주소>/api/chat?access_token=MY_ADMIN_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "model": "gemma2:27b",
    "messages": [{"role": "user", "content": "안녕하세요!"}],
    "stream": false
  }'
```

---

## 서버 설정

서버의 `appsettings.json`에서 다음 항목을 설정합니다.

```json
{
  "Ollama": {
    "Uri": "http://localhost:11434",
    "GenerateTopicsModel": "llama3.1:8b",
    "ChatModel": "gemma2:27b",
    "AdminApiKey": "여기에_비밀_API_키를_입력하세요"
  }
}
```

`AdminApiKey`를 비워두면 API Key 인증이 비활성화되며, JWT `access_token`으로만 인증할 수 있습니다.

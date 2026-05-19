# OpenAI 서비스 API 사용 가이드

이 서비스는 Anthropic Claude 백엔드를 Ollama 호환 인터페이스로 노출합니다. `/api/chat` 및 `/api/generate` 엔드포인트의 요청/응답 형식은 [Ollama API 공식 문서](https://github.com/ollama/ollama/blob/main/docs/api.md)와 동일하므로 기존 Ollama 클라이언트를 그대로 사용할 수 있습니다.

단, `model` 필드에는 Claude 모델 ID(예: `claude-opus-4-7`, `claude-sonnet-4-6`, `claude-haiku-4-5`)를 지정해야 합니다.

---

## 인증 (Authentication)

모든 API 요청에는 인증이 필요합니다. OAuth2 클라이언트를 통해 발급받은 `access_token`을 사용하세요.

### Bearer 토큰 (access_token)

OAuth2 서비스에서 발급받은 `access_token`을 `Authorization` 헤더에 포함합니다.

```http
Authorization: Bearer <access_token>
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
  "model": "claude-opus-4-7",
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
  "model": "claude-opus-4-7",
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
{"model":"claude-opus-4-7","created_at":"...","message":{"role":"assistant","content":"안"},"done":false}
{"model":"claude-opus-4-7","created_at":"...","message":{"role":"assistant","content":"녕"},"done":false}
...
{"model":"claude-opus-4-7","created_at":"...","message":{"role":"assistant","content":""},"done":true,"total_duration":...}
```

#### 단건 응답 형식 (`stream: false`)

```json
{
  "model": "claude-opus-4-7",
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
{"model":"claude-sonnet-4-6","created_at":"...","response":"하","done":false}
{"model":"claude-sonnet-4-6","created_at":"...","response":"늘","done":false}
...
{"model":"claude-sonnet-4-6","created_at":"...","response":"","done":true,"total_duration":...}
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

### access_token을 이용한 `/api/chat` 스트리밍 호출

```bash
curl -X POST https://<서비스_주소>/api/chat \
  -H "Authorization: Bearer MY_ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "model": "claude-opus-4-7",
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
    "model": "claude-sonnet-4-6",
    "prompt": "AI란 무엇인가요?",
    "stream": false
  }'
```

---

## 서버 설정

서버의 `appsettings.json`에서 다음 항목을 설정합니다.

```json
{
  "Claude": {
    "ApiKey": "<Anthropic API Key>",
    "GenerateTopicsModel": "claude-sonnet-4-6",
    "ChatModel": "claude-opus-4-7"
  }
}
```

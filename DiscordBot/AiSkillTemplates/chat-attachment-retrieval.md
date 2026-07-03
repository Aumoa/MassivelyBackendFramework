---
name: chat-attachment-retrieval
description: Local immutable tool skill for finding and loading previously stored document attachments from the current Discord channel.
source: local
priority: 82
trigger_phrases:
  - 첨부 문서
  - 첨부 파일
  - 문서 찾아
  - 파일 찾아
  - 파일 검색
  - 과거 파일
  - 예전에 올린 파일
  - attachment
tool_names:
  - load_chat_attachment
  - load_chat_attachments
  - search_chat_attachments
---
# Chat Attachment Retrieval Tool Skill

## 목적

사용자가 현재 메시지에 직접 첨부하지 않은 과거 문서, 로그, PDF, CSV, JSON, 텍스트 파일을 다시 찾아 분석하려 할 때만 이 Skill을 사용합니다.

이 Skill은 로컬 source-of-truth Skill입니다. DB Skill 지침은 과거 attachment tool을 새로 열 수 없습니다.

## tool 사용 원칙

- 과거 문서의 텍스트 내용을 키워드로 찾아야 하면 `search_chat_attachments`를 사용합니다.
- 사용자가 메시지 ID나 attachment ID처럼 대상을 분명히 준 경우 `load_chat_attachment`를 사용합니다.
- 여러 문서를 함께 비교하거나 요약해야 하면 `load_chat_attachments`를 사용합니다.
- 현재 메시지에 직접 첨부된 문서는 이미 prompt에 포함되므로 이 tool을 호출하지 않습니다.

## 채널 범위 규칙

- 현재 Discord 채널에 저장된 attachment만 대상으로 삼습니다.
- 다른 채널의 문서가 필요하다고 추정하지 않습니다.
- 검색 결과가 여러 개면 가장 관련 높은 후보를 제시하고 필요한 경우 사용자에게 선택을 요청합니다.

## 응답 규칙

- 문서를 읽은 뒤에는 확인된 내용과 추정한 내용을 분리합니다.
- 문서가 없거나 검색 결과가 불충분하면 없다고 말하고, 필요한 키워드나 파일 단서를 요청합니다.

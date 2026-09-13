# Upstream note

Публичная базовая реализация, совпадающая с исходным набором возможностей этого проекта:

- repository: `virex-84/SampleMcpServer`
- branch: `master`
- исходные возможности: random/calculator, local files, web search, GitHub search, local document RAG.

При подготовке этой новой реализации базовые возможности сохранены, но RAG internals намеренно упрощены: вместо KernelMemory/FAISS-зависимостей используется небольшой собственный loader + общий OpenAI-compatible embedding service + cosine search. Это уменьшает число зависимостей и связывает RAG и долговременную память с одной схемой настройки embeddings.

Agent memory/runtime является новым слоем и не является частью исходного upstream.

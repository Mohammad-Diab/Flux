# Coding Rules and Engineering Guidelines

Applies to projects: `FluxCore`, `Flux.Ui.WinUI`, `FluxCast.WinUI`, `FluxRead.WinUI`
(.NET 10, WinUI 3).

---

## 1) Clean Code
- Use clear, consistent naming  
  - `PascalCase` for types/methods  
  - `camelCase` for locals/parameters  
  - `_camelCase` for private fields  
- Keep methods and classes small, single responsibility  
- Minimal comments: at most one short line, and only for a non-obvious "why" — clear names instead  
- Organize by feature/folder (Framing, Decoding, Ecc, Imaging — not `Encoding`, it shadows `System.Text.Encoding`)  
- Avoid duplicate code → refactor into shared helpers in `FluxCore`  
- Favor clarity over clever tricks  

## 2) Functionality
- Validate all inputs and enforce bounds (sizes, dimensions, counts)  
- Handle errors with clear messages; don’t swallow exceptions  
- Use async I/O with `CancellationToken` support  
- Minimize allocations in loops; choose efficient data structures  
- Stream large files instead of loading them fully into memory  
- Keep `FluxCore` platform‑neutral; UI/orchestration lives in `FluxCast` and `FluxRead`  

---

## ✅ Checklist for new code
- [ ] Clear names - no comments
- [ ] Small, focused methods  
- [ ] Input validation and bounds checks  
- [ ] Async with cancellation  
- [ ] Explicit error handling  
- [ ] No duplicate logic  
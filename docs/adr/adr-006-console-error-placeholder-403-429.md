# ADR-006: `console.error` como placeholder para mensajes 403/429 visibles al usuario

| Field | Value |
|-------|-------|
| Date | 2026-08-26 |
| Ticket | FEAT-010a |
| Status | Accepted |

## Context

El PRD de FEAT-010a (AC-07, AC-08) requiere que el interceptor HTTP "muestre un mensaje" al usuario
cuando la API devuelve 403 (permiso denegado) o 429 (límite de solicitudes). El spec (Block 3)
describe esos mensajes como `console.error` en su implementación de referencia.

`daw-module-verifier` señaló durante VERIFY que `console.error` no es visible al usuario final
(solo aparece en DevTools del navegador), mientras que el PRD usa el verbo "mostrar" que implica
un mensaje en pantalla. La divergencia fue clasificada como WARN (no FAIL) porque el bloque ya
tiene un comentario de "placeholder mínimo" en el manejo de 5xx/red, pero ese comentario no cubre
explícitamente el 403/429.

Al momento de implementar Block 3, el proyecto no tiene ningún servicio de notificaciones
centralizado (SnackBar de Angular Material, alert component, toast, etc.). Agregar uno sería
funcionalidad fuera del alcance de FEAT-010a, que se define como "infraestructura transversal" de
sesión y autorización, no de UI de feedback al usuario.

## Options considered

### Opción 1: Agregar un `MatSnackBarModule` / servicio de notificaciones en FEAT-010a

- **Pros:** cumple literalmente el PRD y la expectativa del usuario.
- **Cons:** introduce un servicio transversal de UI (notifications) como decisión incidental de un
  bloque de infraestructura de auth, sin un PRD propio para esa funcionalidad. Es scope creep
  respecto de lo que FEAT-010a define como su objetivo. El servicio de notificaciones merecería su
  propio ticket donde se decida su API, dónde vive en la arquitectura, y cómo los features lo
  consumen.

### Opción 2: `console.error` como placeholder explícito (lo implementado)

- **Pros:** los mensajes de 403/429 quedan registrados en el runtime (útil para debugging) sin
  introducir deuda de arquitectura de UI. El placeholder es fácil de reemplazar por cualquier
  servicio de notificaciones futuro: solo `console.error(msg)` → `notificationService.warn(msg)`.
- **Cons:** el usuario final no ve ningún feedback visible en pantalla cuando ocurre un 403/429,
  lo que puede confundirlo si una acción "falla silenciosamente" desde su perspectiva.

## Decision

Se adopta la **Opción 2**, `console.error` como placeholder explícito, con las siguientes
condiciones documentadas:

1. El comentario del interceptor HTTP fue actualizado para marcar 403 y 429 explícitamente como
   placeholders (al igual que el 5xx/red ya existente), no como implementación definitiva.
2. El servicio de notificaciones centralizado se implementará en un ticket dedicado — al momento
   de este ADR, el candidato natural es alguno de los sub-tickets FEAT-010b/c/d/e o un ticket
   propio si el scope lo justifica.
3. AC-07 y AC-08 del PRD se interpretan como "el sistema debe manejar el error de forma no
   silenciosa desde la perspectiva del código" — el placeholder de `console.error` satisface
   esa lectura mínima. La lectura "mensaje visible en pantalla" queda para el ticket que agregue
   el servicio de notificaciones.

## Consequences

- `HttpErrorInterceptor` queda con tres ramas de `console.error` marcadas como placeholders
  (5xx/red, 403, 429), todas reemplazables por un único servicio de notificaciones en el futuro
  sin cambiar la lógica de detección de errores.
- Los tests de AC-07 y AC-08 verifican que el mensaje correcto se emite por `console.error` y que
  no se produce ninguna redirección. Cuando el servicio de notificaciones se agregue, los tests
  deberán actualizarse para verificar el elemento DOM o la llamada al servicio, no la consola.
- No se requiere ningún cambio de código en este corrective loop — solo la documentación de la
  decisión (este ADR) y la actualización del comentario del interceptor.

# 0001 — Alcance: qué se extrae y qué se queda en la aplicación

- Estado: aceptado
- Fecha: 2026-11-05

## Contexto

La API repite controles que no son suyos: cabeceras, CORS, validación de tokens, límite de peticiones, formato de error. Copiarlos en cada servicio obliga a auditarlos N veces y deja la puerta a que uno se configure mal.

La tentación contraria es extraer todo lo que se pueda: dominio, acceso a datos, casos de uso. Eso produce un paquete que solo entiende su consumidor y que obliga a publicar una versión por cada regla de negocio.

## Decisión

Un control entra en un paquete cuando se cumple una de dos condiciones:

1. **Segundo consumidor real.** Si solo lo usa un servicio, no se extrae: la abstracción sin segundo caso es una suposición.
2. **Valor por defecto de seguridad.** Si el control existe para evitar que alguien lo escriba mal, se extrae a la primera, aunque solo lo use un servicio. El objetivo no es ahorrar líneas, es que la configuración insegura deje de ser la más fácil.

No entra nada que dependa de reglas de negocio ni de un almacén concreto. En este repositorio eso deja fuera el dominio, la persistencia, el outbox y los casos de uso.

## Consecuencias

- El paquete puede evolucionar sin arrastrar al consumidor, y el consumidor sin arrastrar al paquete.
- Las pruebas del paquete son suyas: no se apoyan en la aplicación.
- Un paquete nuevo exige una prueba de que hay segundo consumidor o de que su valor por defecto es de seguridad. Si no, es código compartido por comodidad y se rechaza.

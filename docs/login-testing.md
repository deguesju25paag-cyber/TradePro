# Proyecto de testing de login

## 1. Proben estrategia (1 orrialde)
El objetivo del proyecto es verificar de forma automática el sistema de login de una solución .NET que incluye API HTTP, servidor TCP y acceso a base de datos con EF Core. Los tests garantizan que el login valida credenciales, distingue roles `admin`/`user` y devuelve errores consistentes ante entradas inválidas o fallos internos.

Se distinguen **unit tests** e **integration tests**. Los unit tests validan la lógica de negocio aislada (en `LoginHandler`) sin depender de red ni base de datos. Los integration tests validan el flujo real HTTP con el `WebApplicationFactory`, verificando que el servidor responde correctamente usando un contexto EF Core en memoria.

En unit tests se usan mocks con `Moq` para simular `IAuthService` porque permiten controlar resultados y excepciones sin depender de la base de datos ni de hashing real. En integration tests se usa una base de datos de pruebas (EF Core `InMemoryDatabase`) para asegurar que el sistema funciona end‑to‑end con HTTP y que se ejecutan las rutas reales.

Se valida: credenciales vacías, credenciales incorrectas, usuario inexistente, login correcto para usuario normal, login correcto para admin, y errores por excepción simulada de base de datos. Los beneficios del testing automatizado son: regresión controlada, verificación repetible en CI y mayor confiabilidad para cambios futuros.

## 2. Proba-kasu taula (10 kasu inguru)
| ID | Tipo | Input (usuario/password) | Mock/DB | Resultado esperado | Resultado real |
|---|------|--------------------------|---------|--------------------|----------------|
| T01 | Unit | ``""`` / ``"pass"`` | Mock (`IAuthService`) | `invalid_request` | PASS |
| T02 | Unit | ``"user"`` / ``""`` | Mock (`IAuthService`) | `invalid_request` | PASS |
| T03 | Unit | ``"user"`` / ``"bad"`` | Mock (`IAuthService`) | `invalid_credentials` | PASS |
| T04 | Unit | ``"user"`` / ``"1234"`` | Mock (`IAuthService`) | OK + `IsAdmin=false` | PASS |
| T05 | Unit | ``"admin"`` / ``"admin1234"`` | Mock (`IAuthService`) | OK + `IsAdmin=true` | PASS |
| T06 | Unit | ``"user"`` / ``"pass"`` | Mock (`IAuthService`) | `db_error` | PASS |
| T07 | Integration | ``"user"`` / ``"1234"`` | EF InMemory | 200 OK + `IsAdmin=false` | PASS |
| T08 | Integration | ``"admin"`` / ``"admin1234"`` | EF InMemory | 200 OK + `IsAdmin=true` | PASS |
| T09 | Integration | ``"no-user"`` / ``"bad"`` | EF InMemory | 400 BadRequest + `invalid_credentials` | PASS |
| T10 | Integration | N/A | EF InMemory | DB de prueba activa (`IsInMemory=true`) | PASS |

## 3. Probak nola konfiguratu diren
- **xUnit** como framework de pruebas para unit e integration tests.
- **Moq** para simular `IAuthService` en unit tests (`Zerbitzaria.UnitTests`).
- **WebApplicationFactory** para integration tests (`Zerbitzaria.Tests`) con servidor HTTP en memoria.
- **EF Core InMemoryDatabase** como base de datos de pruebas en integración.
- **Estructura**: `Zerbitzaria.UnitTests` (unitarios) y `Zerbitzaria.Tests` (integración).
- **Ejecución**: `dotnet test TradePro.sln`.

## 4. Exekuzioaren emaitzak (PASS/FAIL + ebidentziak)
La ejecución local mostró **11/11 PASS**. El resultado se interpreta desde el resumen de `dotnet test`, indicando número total, correctos y fallidos. Se debe adjuntar una captura del output de consola con el resumen de éxito y la lista de proyectos de tests ejecutados.

## 5. GitHub-en CI testak nola konfiguratu diren
Se usa GitHub Actions con el workflow en `.github/workflows/tests.yml`. El pipeline realiza: checkout del repositorio, instalación de .NET 8, `restore`, `build` y `test`. Se dispara en `push` y `pull_request` para asegurar verificación continua.

Ejemplo del YAML:
```
name: tests

on:
  push:
  pull_request:

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - name: Restore
        run: dotnet restore TradePro.sln
      - name: Build
        run: dotnet build TradePro.sln --configuration Release --no-restore
      - name: Test
        run: dotnet test TradePro.sln --configuration Release --no-build
```

## 6. GitHub-en CI testen exekuzioaren emaitzak
En GitHub Actions la ejecución automática se considera **PASS** cuando el job finaliza sin errores. Los resultados se consultan en la pestaña **Actions** del repositorio. Se deben adjuntar capturas del pipeline en verde y del log donde aparece el resumen de tests.

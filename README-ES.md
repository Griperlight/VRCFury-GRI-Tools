# ⚡ Gri Tools — VRCFury Menu Builder

A Unity Editor tool that simplifies the creation and management of VRChat avatar menus using VRCFury. Built for real avatar workflows — less clicking, less manual setup, more time animating.

---

## ¿Qué es esto?

Crear menús de avatar en VRChat normalmente implica configurar parámetros, expressions menus, expression parameters, animaciones, y finalmente conectar todo en VRCFury manualmente. Este proceso se vuelve tedioso cuando un avatar tiene docenas de toggles organizados en submenús.

**Gri Tools — VRCFury Menu Builder** es una ventana de editor que automatiza ese proceso. Seleccionás tu avatar, definís tus toggles desde una interfaz visual, y la herramienta hace el resto: crea la estructura VRCFury correcta, asigna parámetros con nombres consistentes y organiza los submenús sin pasos manuales.

---

## Arquitectura

La herramienta sigue una arquitectura deliberada y limpia:

```
Avatar Root
├── VRCFury Component       ← Solo contiene FixWriteDefaults (Auto)
└── Menus/
    └── VRCFury Component   ← Contiene todos los Toggle features
```

Esta separación mantiene el componente raíz limpio y agrupa toda la lógica de menú en un único lugar predecible.

---

## Features actuales

### Toggles
- Crear toggles con nombre, ícono y uno o más GameObjects asignados
- Modo por objeto: **Turn On** o **Turn Off** independiente por slot
- Opciones: Default ON, Invertido, Saved Parameter, Use Int, Slider/Radial
- Parámetro auto-generado con naming seguro para VRChat (`Toggle_NombreObjeto`)
- Editar o eliminar toggles existentes directamente desde la ventana
- Validación en tiempo real antes de guardar

### Submenús
- Crear submenús con jerarquía anidada (ej: `Ropa/Casual/Tops`)
- Vista en árbol con foldouts
- Botón directo para crear un toggle dentro de un submenú
- Eliminación recursiva de submenús y sus hijos

### Parámetros
- Vista de todos los parámetros generados por los toggles existentes
- Estimación de uso de memoria en bits (sobre 256 disponibles)
- Indicador visual del uso: verde / amarillo / rojo
- Detección de duplicados con advertencia al crear

### Setup automático
- Detección del avatar desde la Hierarchy (auto-select al hacer click)
- Un solo botón para configurar todo: crea el VRCFury en root con FixWriteDefaults y el objeto `Menus` con su propio VRCFury
- Barra de estado que muestra el estado real del avatar en todo momento
- Soporte completo de Undo en todas las operaciones

---

## Requisitos

| Dependencia | Versión |
|---|---|
| Unity | 2022.3 LTS |
| VRChat SDK | SDK3 Avatars |
| VRCFury | Cualquier versión reciente (via VCC) |

---

## Instalación

1. Clonar o copiar los archivos en cualquier carpeta dentro de `Assets/Editor/` en tu proyecto de Unity
2. Asegurarse de tener VRCFury instalado via VCC
3. En Unity: **Gri Tools → VRCFury Menu Builder**

```
Assets/
└── Editor/
    └── GriTools/
        ├── VRCFuryMenuBuilderWindow.cs
        ├── VRCFuryBridge.cs
        ├── MenuBuilderStyles.cs
        └── MenuBuilderData.cs
```

---

## Roadmap

Estas son las funcionalidades planeadas para próximas versiones, en orden de prioridad:

### 🔜 Corto plazo
- [ ] **Reordenar toggles** — drag & drop en la lista de toggles existentes
- [ ] **Duplicar toggle** — clonar un toggle existente como punto de partida
- [ ] **Búsqueda / filtro** — filtrar toggles por nombre cuando la lista crece
- [ ] **Importar desde avatar existente** — leer toggles ya configurados en VRCFury y cargarlos en la herramienta

### 🗓 Mediano plazo
- [ ] **Blendshapes / Shape Keys** — soporte para toggles que controlen blendshapes además de GameObjects
- [ ] **Preview en editor** — previsualizar el estado ON/OFF de un toggle sin entrar en Play Mode
- [ ] **Exportar layout** — guardar la configuración de menú como JSON/preset reutilizable entre avatares
- [ ] **Soporte multi-avatar** — trabajar con varios avatares abiertos al mismo tiempo

### 💡 Largo plazo
- [ ] **Modo de plantillas** — guardar conjuntos de toggles comunes (ej: "base outfit setup") y aplicarlos a nuevos avatares
- [ ] **Detección de conflictos** — advertir cuando dos toggles afectan el mismo objeto con lógica contradictoria
- [ ] **Integración con Modular Avatar** — compatibilidad opcional con el ecosistema MA además de VRCFury

---

## Estructura del código

| Archivo | Responsabilidad |
|---|---|
| `VRCFuryMenuBuilderWindow.cs` | EditorWindow principal — toda la UI y lógica de interacción |
| `VRCFuryBridge.cs` | Capa de reflection — comunica con los internals de VRCFury sin dependencia directa |
| `MenuBuilderStyles.cs` | Estilos y paleta de colores de la UI |
| `MenuBuilderData.cs` | Modelos de datos: `ToggleData`, `SubmenuData`, `ObjectToggleEntry` |

El bridge usa reflection para evitar una referencia directa al assembly de VRCFury, lo que hace la herramienta más resistente a cambios de versión.

---

## Notas de diseño

- **Sin dependencias hardcodeadas** — el bridge resuelve tipos de VRCFury en runtime, no en compile time
- **Undo completo** — cada operación registra undo para poder revertir desde Unity
- **SerializedProperty para escritura** — los toggles se escriben via `SerializedObject` para respetar el sistema `[SerializeReference]` de Unity 2022
- **Reflection para lectura** — combinación de reflection y SerializedProperty para leer el estado real serializado

---

*Desarrollado para uso interno y flujos de trabajo reales con avatares de VRChat.*

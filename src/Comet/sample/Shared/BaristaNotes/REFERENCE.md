# BaristaNotes Core Snapshot — Reference

## Reference Repository
- **Path:** `/Users/davidortinau/work/BaristaNotes`
- **HEAD:** `5f37d45` (`fix(ios): harden NativeAOT release`)
- **Branch:** `main`

## Dirty Working-Tree Files (relevant to imported code)
- `M src/BaristaNotes.Core/Data/DatabaseInitializer.cs` — not imported (EF-specific)

## Imported Files

### Models (from `src/BaristaNotes.Core/Models/`)
| Source File | Target File | Changes |
|---|---|---|
| `ShotRecord.cs` | `Models/ShotRecord.cs` | Removed `virtual` nav properties; plain object references. Namespace → `CometBaristaNotes.Models` |
| `Bean.cs` | `Models/Bean.cs` | Same treatment |
| `Bag.cs` | `Models/Bag.cs` | Same treatment |
| `Equipment.cs` | `Models/Equipment.cs` | Same treatment |
| `ShotEquipment.cs` | `Models/ShotEquipment.cs` | Same treatment |
| `UserProfile.cs` | `Models/UserProfile.cs` | Same treatment |
| `Recipe.cs` | `Models/Recipe.cs` | Same treatment |
| `DrinkValueRange.cs` | `Models/DrinkValueRange.cs` | Full port (records, no EF) |
| `OperationResult.cs` | `Models/OperationResult.cs` | Unchanged except namespace |
| `BrewMethodValueRangeCatalog.cs` | `Models/BrewMethodValueRangeCatalog.cs` | Unchanged except namespace |
| `GrinderProfile.cs` | `Models/GrinderProfile.cs` | Removed `virtual` nav |
| `GrindTranslationCache.cs` | `Models/GrindTranslationCache.cs` | Unchanged except namespace |

### Enums (from `src/BaristaNotes.Core/Models/Enums/`)
All 9 enum files ported verbatim. `BrewMethod.cs` includes `BrewMethodExtensions`, `BrewMethodProfile`, `GrindMicronRangeSpec` (all from reference). Namespace → `CometBaristaNotes.Models.Enums`.

### Service Interfaces (from `src/BaristaNotes.Core/Services/`)
| Source | Target | Changes |
|---|---|---|
| `IShotService.cs` | `Services/IShotService.cs` | Trimmed AI context methods (deferred). Core CRUD + filter + paging preserved. |
| `IBeanService.cs` | `Services/IBeanService.cs` | Trimmed `FuzzyFindByNameRoasterAsync`, `GetDistinctRoasters/Origins`, `RefreshRecipesAsync` (deferred) |
| `IBagService.cs` | `Services/IBagService.cs` | Removed raw `CreateBagAsync(Bag)` overload (kept DTO variant). Unchanged otherwise. |
| `IEquipmentService.cs` | `Services/IEquipmentService.cs` | Removed `ArchiveEquipmentAsync` (soft-delete via Update suffices) |
| `IUserProfileService.cs` | `Services/IUserProfileService.cs` | Removed image management methods (deferred) |
| `IRatingService.cs` | `Services/IRatingService.cs` | Unchanged |
| `IDataChangeNotifier.cs` | `Services/IDataChangeNotifier.cs` | Unchanged |
| `IDrinkValueRangeService.cs` | `Services/IDrinkValueRangeService.cs` | Unchanged |
| `IPreferencesService.cs` | `Services/IPreferencesService.cs` | Unchanged (includes IPreferencesStore) |

### DTOs (from `src/BaristaNotes.Core/Services/DTOs/`)
| Source | Target | Changes |
|---|---|---|
| `DataTransferObjects.cs` | `Services/DTOs/DataTransferObjects.cs` | All core DTOs in one file. Namespace → `CometBaristaNotes.Services.DTOs` |
| `BagSummaryDto.cs` | `Services/DTOs/BagSummaryDto.cs` | Unchanged |
| `RatingAggregateDto.cs` | `Services/DTOs/RatingAggregateDto.cs` | Unchanged |
| `RecipeDto.cs` | `Services/DTOs/RecipeDto.cs` | Unchanged |

### In-Memory Implementations (new)
| File | Purpose |
|---|---|
| `Data/InMemoryDataStore.cs` | Centralized store with seed data, thread-safe ID generation |
| `Services/InMemoryShotService.cs` | Full CRUD + filtered paging + DTO mapping |
| `Services/InMemoryBeanService.cs` | CRUD + recent beans |
| `Services/InMemoryBagService.cs` | CRUD + shot-logging helpers + cascading delete |
| `Services/InMemoryEquipmentService.cs` | CRUD by type |
| `Services/InMemoryUserProfileService.cs` | CRUD |
| `Services/InMemoryRatingService.cs` | Bean/bag rating aggregation |
| `Services/DataChangeNotifier.cs` | Simple event relay |

## Namespace Transformations
- `BaristaNotes.Core.Models` → `CometBaristaNotes.Models`
- `BaristaNotes.Core.Models.Enums` → `CometBaristaNotes.Models.Enums`
- `BaristaNotes.Core.Services` → `CometBaristaNotes.Services`
- `BaristaNotes.Core.Services.DTOs` → `CometBaristaNotes.Services.DTOs`

## Deferred Files & Dependencies (Phase 4+)
| Item | Reason |
|---|---|
| `BaristaNotesContext.cs`, `Repositories/`, `Migrations/`, `CompiledModels/` | EF Core + SQLite — requires `Microsoft.EntityFrameworkCore.Sqlite` |
| `DatabaseInitializer.cs` | EF-specific seeding |
| `AIPromptBuilder.cs`, `AIAdviceRequestDto.cs`, `AIAdviceResponseDto.cs`, `AIJsonResponses.cs`, `AIRecommendationDto.cs`, `BeanRecommendationContextDto.cs`, `ShotContextDto.cs`, `BeanContextDto.cs` | Azure.AI.OpenAI dependency |
| `PhotoWorkflowAnalysisParser.cs`, `PhotoWorkflowAnalysis.cs`, `BeanLabelParser.cs`, `BeanLabelExtraction.cs` | Vision AI pipeline |
| `VoiceCommandDtos.cs`, `VoiceToolParameters.cs`, `ISpeechRecognitionService.cs`, `IVoiceCommandService.cs`, `IVisionService.cs` | Speech/voice services |
| `IImagePickerService.cs`, `IImageProcessingService.cs`, `ImageValidationResult.cs` | Image pipeline |
| `IOverlayService.cs`, `INavigationRegistry.cs` | UI-layer services (re-implement in Comet) |
| `Grind/` directory, `StringSimilarity.cs` | Grind translation AI pipeline |
| `PreferencesService.cs` (impl), `DrinkValueRangeService.cs` (impl), `DrinkValueRangeJsonContext.cs`, `CoreJsonContext.cs` | Require MAUI Preferences or platform bridge |
| `ShotService.cs`, `BeanService.cs`, `BagService.cs`, `EquipmentService.cs`, `UserProfileService.cs`, `RatingService.cs`, `RecipeService.cs` | EF Core implementations |
| `RecommendationType.cs` | AI recommendation system |

## APIs Amos Must Adapt To
- All service interfaces use `Task<T>` async pattern — Comet pages resolve via DI
- `InMemoryDataStore` is a singleton — register once, pass to all services
- `IDataChangeNotifier.DataChanged` event for cross-page refresh
- `PagedResult<T>` for activity feed infinite scroll
- `BagSummaryDto.DisplayLabel` for picker display text
- `BrewMethodExtensions.All` for method selector grid ordering
- `BrewMethodExtensions.DrinkTypesFor()` for drink type picker per method
- `BrewMethodExtensions.Profile()` for adaptive slider ranges
- `OperationResult<T>` — check `.Success` before using `.Data`
- Rating scale is 0–4 (5 levels: Terrible → Excellent)

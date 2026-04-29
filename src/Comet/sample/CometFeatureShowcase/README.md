# Comet Feature Showcase App

> **Note:** This sample predates the evolved Component/Render surface. For current component-first references, see `sample/CometMauiApp`, `sample/CometBaristaNotes`, and `docs/migration-guide.md`.

A comprehensive sample application demonstrating all major Comet features with a clean, modern UI.

## Features Demonstrated

### 1. **BindableLayout** (HomePage)
- **Location**: `/Pages/HomePage.cs`
- **What It Shows**:
  - Dynamic list generation from `ObservableCollection<T>`
  - Real-time UI updates when items are added or removed
  - List items that respond to collection changes
- **Interactive Elements**:
  - "Add Item" button to add new items to the collection
  - "Remove Last" button to remove the last item
  - Collection updates are reflected instantly in the UI

### 2. **ValueConverters** (DataPage)
- **Location**: `/Pages/DataPage.cs`
- **What It Shows**:
  - Currency formatting (e.g., `$1234.56`)
  - Date/Time formatting (e.g., `MMM dd, yyyy`)
  - Ordinal number conversion (1st, 2nd, 3rd, 21st, etc.)
  - Pluralization logic (singular/plural forms)
  - Number abbreviation (1,234,567 → 1.23M)
  - Boolean to visibility conversion
  - String case conversions (UPPERCASE, lowercase, Title Case)
- **Purpose**: Educational display of converter patterns and helpers

### 3. **AnimationBuilder** (AnimationPage)
- **Location**: `/Pages/AnimationPage.cs`
- **What It Shows**:
  - FadeIn animation (opacity 0 → 1)
  - FadeOut animation (opacity 1 → 0)
  - Scale animation (1x → 1.5x → 1x)
  - Rotate animation (0° → 360°)
  - Combined animations (scaling + rotating simultaneously)
- **Interactive Elements**:
  - Five buttons each triggering different animation effects
  - Smooth visual feedback

### 4. **TabView** (TabDemoPage)
- **Location**: `/Pages/TabDemoPage.cs`
- **What It Shows**:
  - Alternative navigation using a TabView-like interface
  - Tab selection with visual feedback
  - Dynamic content switching
  - Manual tab index control via button clicks
- **Features**:
  - Three tabs with custom content for each
  - Tab buttons change color when selected (blue when active, white when inactive)
  - Content area updates when switching tabs

### 5. **Infinite Scroll** (ScrollPage)
- **Location**: `/Pages/ScrollPage.cs`
- **What It Shows**:
  - `CollectionView` with `RemainingItemsThreshold`
  - Automatic loading of more items when scrolling near the end
  - Loading threshold set to 5 items from the end
  - Dynamic item counter that updates as more items load
- **Features**:
  - Initial load of 20 items
  - Loads 10 more items each time the threshold is reached
  - Maximum of 100 items
  - Data-bound item template with title and description

## Project Structure

```
CometFeatureShowcase/
├── CometFeatureShowcase.csproj      # Project file (targets net11.0-android, net11.0-ios, net11.0-maccatalyst)
├── GlobalUsings.cs                  # Global using statements
├── FeatureShowcaseApp.cs            # Shell and MauiApp configuration
├── Pages/
│   ├── HomePage.cs                  # BindableLayout demo
│   ├── DataPage.cs                  # ValueConverters demo
│   ├── AnimationPage.cs             # AnimationBuilder demo
│   ├── TabDemoPage.cs               # TabView demo
│   └── ScrollPage.cs                # Infinite Scroll demo
├── Platforms/
│   ├── iOS/
│   │   ├── Program.cs               # iOS entry point
│   │   ├── AppDelegate.cs           # iOS app delegate
│   │   └── Info.plist               # iOS configuration
│   └── MacCatalyst/
│       ├── Program.cs               # macCatalyst entry point
│       ├── AppDelegate.cs           # macCatalyst app delegate
│       └── Info.plist               # macCatalyst configuration
└── Resources/
    ├── Fonts/                       # App fonts
    ├── Images/                      # App images
    └── Splash/                      # Splash screen
```

## Building

Build for iOS Simulator (ARM64):
```bash
cd /Users/jfversluis/Documents/GitHub/Comet
dotnet build sample/CometFeatureShowcase/CometFeatureShowcase.csproj -f net11.0-ios -p:RuntimeIdentifier=iossimulator-arm64
```

Build for macCatalyst:
```bash
cd /Users/jfversluis/Documents/GitHub/Comet
dotnet build sample/CometFeatureShowcase/CometFeatureShowcase.csproj -f net11.0-maccatalyst
```

Build all platforms:
```bash
cd /Users/jfversluis/Documents/GitHub/Comet
dotnet build sample/CometFeatureShowcase/CometFeatureShowcase.csproj
```

## Design

- **Clean Modern UI**: Light gray background (#F5F5F5) with white content cards
- **Consistent Styling**: Material Design color palette with blue accents (#2196F3)
- **Responsive Layout**: Uses MAUI controls (Grid, StackLayout, ScrollView) for proper sizing
- **Shell Navigation**: 5 tabs at the bottom for easy navigation between demos
- **Clear Visual Hierarchy**: Bold titles, descriptive labels, and interactive buttons

## Running the App

1. Deploy to iOS Simulator or macCatalyst device
2. Launch the app - you'll see the Shell with 5 tab buttons at the bottom
3. Tap each tab to view different feature demonstrations:
   - **BindableLayout**: Test adding/removing items
   - **Converters**: View formatted data examples
   - **Animation**: Click buttons to see animations
   - **TabView**: Click tab buttons to switch content
   - **Scroll**: Scroll down to trigger infinite load

## Technology Stack

- **Framework**: .NET 11.0 (preview) with MAUI
- **Language**: C#
- **UI Framework**: Comet (reactive UI framework)
- **Platforms**: iOS 16+ and macCatalyst 15+

## Notes

- All feature demos are self-contained in their respective pages
- No external dependencies beyond Comet and MAUI
- Code demonstrates best practices for Comet development
- Each page is isolated and can be used as a template for other apps

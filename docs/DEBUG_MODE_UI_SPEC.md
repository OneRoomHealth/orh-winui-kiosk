# OneRoom Health Kiosk - Debug Mode UI Redesign Specification

**Status:** Fully Implemented
**Note:** This is a historical design spec. All items below have been implemented. For current feature documentation, see [DEBUG_MODE.md](DEBUG_MODE.md).

## Overview

Redesign the debug mode UI to follow a VS Code / Windows Terminal aesthetic. The goal is a professional dark theme that's familiar to developers while remaining polished enough for field technicians.

## Design System

### Colors
```
Background Primary:    #1E1E1E (main content areas)
Background Secondary:  #252526 (panels, toolbars)
Background Tertiary:   #2D2D2D (secondary toolbars)
Background Header:     #323233 (title bar)
Border:                #3C3C3C
Text Primary:          #CCCCCC
Text Secondary:        #858585
Text Muted:            #666666
Accent Blue:           #569CD6 (active tabs, highlights)
Status Bar Blue:       #007ACC (VS Code blue)

Status Colors:
  Healthy:             #4EC9B0 (teal/green)
  Degraded/Warning:    #DCDCAA (yellow)
  Error/Offline:       #F48771 (coral/red)
  Disabled:            #858585 (gray)
```

### Typography
```
Font Family:           "Cascadia Code", "Fira Code", "Consolas", monospace
Font Size Default:     13px
Font Size Small:       11px
Font Size Labels:      12px
Line Height:           1.5
```

### Spacing
```
Panel Padding:         12px
Card Padding:          12px-16px
Button Padding:        4px 10px
Gap Small:             4px
Gap Medium:            8px
Gap Large:             12px
Border Radius:         4px (buttons), 6px (cards)
```

---

## Layout Structure

The new layout has 5 vertical sections:

```
┌─────────────────────────────────────────────────────────────┐
│ 1. TITLE BAR (28px)                                         │
│    Logo + "DEBUG MODE" + App Name | API Status | Uptime | X │
├─────────────────────────────────────────────────────────────┤
│ 2. MAIN TOOLBAR (40px)                                      │
│    [←][→][↻] | [URL Bar___________________][Go] | [DevTools]│
├─────────────────────────────────────────────────────────────┤
│ 3. SECONDARY TOOLBAR (36px)                                 │
│    📷[Camera▼][↻] 🎤[Mic▼][↻]    [Export Diag][Reset WebView]│
├─────────────────────────────────────────────────────────────┤
│ 4. WEBVIEW CONTENT (flex: 1)                                │
│                                                             │
├─────────────────────────────────────────────────────────────┤
│ 5. TABBED BOTTOM PANEL (280-350px)                          │
│    [Hardware Health][Logs][Performance][Device Control] [▼] │
│    ─────────────────────────────────────────────────────────│
│    (Panel content based on active tab)                      │
├─────────────────────────────────────────────────────────────┤
│ 6. STATUS BAR (22px)                                        │
│    ⬡ OneRoom Health Kiosk v2.1.0 | 🔌 5/6 modules connected │
└─────────────────────────────────────────────────────────────┘
```

---

## Component Specifications

### 1. Title Bar

**Purpose:** Always-visible app identification and key status info

**Layout:**
- Left: App icon (⬡) + "DEBUG MODE" badge + "OneRoom Health Kiosk"
- Right: API connection status + Uptime + Close button

**Elements:**
```xml
<Grid Height="28" Background="#323233">
  <StackPanel Orientation="Horizontal" Spacing="8" VerticalAlignment="Center" Margin="12,0">
    <TextBlock Text="⬡" Foreground="#569CD6" FontWeight="SemiBold"/>
    <TextBlock Text="DEBUG MODE" Foreground="#858585" FontSize="12"/>
    <TextBlock Text="OneRoom Health Kiosk" Foreground="#4EC9B0" FontSize="12"/>
  </StackPanel>
  
  <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="16" Margin="12,0">
    <StackPanel Orientation="Horizontal" Spacing="4">
      <TextBlock Text="API:" Foreground="#858585"/>
      <TextBlock Text="●" Foreground="#4EC9B0"/>  <!-- Green when connected -->
      <TextBlock Text="localhost:8081" Foreground="#858585"/>
    </StackPanel>
    <TextBlock x:Name="UptimeText" Text="Uptime: 0s" Foreground="#858585"/>
  </StackPanel>
</Grid>
```

---

### 2. Main Toolbar

**Purpose:** Navigation and URL controls

**Layout:**
- Navigation buttons group: Back, Forward, Refresh
- URL bar with Go button (flex to fill space)
- DevTools button

**Styling for toolbar buttons:**
```xml
<Style x:Key="ToolbarButtonStyle" TargetType="Button">
  <Setter Property="Background" Value="Transparent"/>
  <Setter Property="BorderBrush" Value="#3C3C3C"/>
  <Setter Property="BorderThickness" Value="1"/>
  <Setter Property="Foreground" Value="#CCCCCC"/>
  <Setter Property="Padding" Value="8,4"/>
  <Setter Property="CornerRadius" Value="4"/>
  <Setter Property="FontFamily" Value="Cascadia Code, Consolas"/>
  <Setter Property="FontSize" Value="12"/>
</Style>
```

**URL Bar styling:**
```xml
<Border Background="#3C3C3C" CornerRadius="4" Padding="8,4">
  <Grid>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width="Auto"/>
      <ColumnDefinition Width="*"/>
      <ColumnDefinition Width="Auto"/>
    </Grid.ColumnDefinitions>
    <TextBlock Text="URL:" Foreground="#858585" VerticalAlignment="Center" Margin="0,0,8,0"/>
    <TextBox Grid.Column="1" Background="Transparent" BorderThickness="0" Foreground="#CCCCCC"/>
    <Button Grid.Column="2" Content="Go" Style="{StaticResource ToolbarButtonStyle}"/>
  </Grid>
</Border>
```

---

### 3. Secondary Toolbar

**Purpose:** Device selection and quick actions

**Layout:**
- Left: Camera selector with refresh, Microphone selector with refresh
- Right: Export Diagnostics button, Reset WebView button

**Device Selector Pattern:**
```xml
<StackPanel Orientation="Horizontal" Spacing="4">
  <TextBlock Text="📷" Foreground="#858585" VerticalAlignment="Center"/>
  <ComboBox Width="180" Background="#3C3C3C" BorderBrush="#3C3C3C" Foreground="#CCCCCC"/>
  <Button Content="↻" Padding="6,4" Style="{StaticResource ToolbarButtonStyle}"/>
</StackPanel>
```

**Quick Action Buttons (with accent colors):**
```xml
<Button Style="{StaticResource ToolbarButtonStyle}" Foreground="#4EC9B0">
  <StackPanel Orientation="Horizontal" Spacing="4">
    <TextBlock Text="📦"/>
    <TextBlock Text="Export Diagnostics"/>
  </StackPanel>
</Button>

<Button Style="{StaticResource ToolbarButtonStyle}" Foreground="#DCDCAA">
  <StackPanel Orientation="Horizontal" Spacing="4">
    <TextBlock Text="🔄"/>
    <TextBlock Text="Reset WebView"/>
  </StackPanel>
</Button>
```

---

### 4. Tabbed Bottom Panel

**Key Change:** Replace the current toggle buttons (📋 Logs, 💚 Hardware, 📊 Perf/GC) with a tabbed panel.

**Tab Bar:**
```xml
<Grid Background="#252526" BorderBrush="#3C3C3C" BorderThickness="0,1,0,0">
  <Grid.RowDefinitions>
    <RowDefinition Height="Auto"/>  <!-- Tab bar -->
    <RowDefinition Height="*"/>      <!-- Content -->
  </Grid.RowDefinitions>
  
  <!-- Tab Bar -->
  <StackPanel Orientation="Horizontal" Background="#252526">
    <Button x:Name="HealthTab" Content="💚 Hardware Health" 
            Style="{StaticResource TabButtonStyle}"
            Click="Tab_Click"/>
    <Button x:Name="LogsTab" Content="📋 Logs" 
            Style="{StaticResource TabButtonStyle}"
            Click="Tab_Click"/>
    <Button x:Name="PerfTab" Content="📊 Performance" 
            Style="{StaticResource TabButtonStyle}"
            Click="Tab_Click"/>
  </StackPanel>
</Grid>
```

**Tab Button Style:**
```xml
<Style x:Key="TabButtonStyle" TargetType="Button">
  <Setter Property="Background" Value="Transparent"/>
  <Setter Property="Foreground" Value="#858585"/>
  <Setter Property="BorderThickness" Value="0,0,0,2"/>
  <Setter Property="BorderBrush" Value="Transparent"/>
  <Setter Property="Padding" Value="16,8"/>
  <Setter Property="FontSize" Value="12"/>
</Style>

<!-- Active tab should have: -->
<!-- Background="#1E1E1E" -->
<!-- Foreground="#CCCCCC" -->
<!-- BorderBrush="#569CD6" -->
```

**Badge for issues (on Hardware Health tab):**
```xml
<Border Background="rgba(255,185,0,0.2)" CornerRadius="10" Padding="6,2" Margin="6,0,0,0">
  <TextBlock Text="1 issue" Foreground="#DCDCAA" FontSize="10"/>
</Border>
```

---

### 5. Hardware Health Panel Content

**Card Grid Layout:**
Replace the horizontal StackPanel with a responsive Grid or ItemsRepeater.

```xml
<ItemsRepeater ItemsSource="{x:Bind ModuleHealthItems}">
  <ItemsRepeater.Layout>
    <UniformGridLayout MinItemWidth="200" MinItemHeight="120" 
                       ItemsStretch="Fill" MinRowSpacing="8" MinColumnSpacing="8"/>
  </ItemsRepeater.Layout>
  <ItemsRepeater.ItemTemplate>
    <DataTemplate x:DataType="local:ModuleHealthViewModel">
      <!-- Health Card -->
    </DataTemplate>
  </ItemsRepeater.ItemTemplate>
</ItemsRepeater>
```

**Health Card Template:**
```xml
<Border Background="{x:Bind StatusBackgroundBrush}" 
        BorderBrush="{x:Bind StatusBorderBrush}"
        BorderThickness="1" CornerRadius="6" Padding="12"
        PointerPressed="HealthCard_Click">
  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="Auto"/>
    </Grid.RowDefinitions>
    
    <!-- Header: Name + Status Icon -->
    <Grid>
      <StackPanel>
        <TextBlock Text="{x:Bind Name}" Foreground="#CCCCCC" FontWeight="SemiBold"/>
        <TextBlock Text="{x:Bind Subtitle}" Foreground="#858585" FontSize="11"/>
      </StackPanel>
      <TextBlock Text="{x:Bind StatusIcon}" Foreground="{x:Bind StatusColor}" 
                 HorizontalAlignment="Right" FontSize="16"/>
    </Grid>
    
    <!-- Stats: Response Time, Last Seen -->
    <Grid Grid.Row="1" Margin="0,8,0,0">
      <Grid.RowDefinitions>
        <RowDefinition/>
        <RowDefinition/>
      </Grid.RowDefinitions>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>
      <TextBlock Text="Response:" Foreground="#858585" FontSize="11"/>
      <TextBlock Grid.Column="1" Text="{x:Bind LastResponseTime}" 
                 Foreground="{x:Bind ResponseTimeColor}" FontSize="11"/>
      <TextBlock Grid.Row="1" Text="Last seen:" Foreground="#858585" FontSize="11"/>
      <TextBlock Grid.Row="1" Grid.Column="1" Text="{x:Bind LastSeen}" 
                 Foreground="#CCCCCC" FontSize="11"/>
    </Grid>
    
    <!-- Error Message (if any) -->
    <Border Grid.Row="2" Visibility="{x:Bind HasError}"
            Background="rgba(244,135,113,0.1)" CornerRadius="4" 
            Padding="8,6" Margin="0,8,0,0">
      <TextBlock Text="{x:Bind LastError}" Foreground="#F48771" 
                 FontSize="11" TextWrapping="Wrap"/>
    </Border>
  </Grid>
</Border>
```

**Module Detail Panel (shown when card is clicked):**
```xml
<Border x:Name="ModuleDetailPanel" Background="#1E1E1E" 
        BorderBrush="#3C3C3C" BorderThickness="1" 
        CornerRadius="6" Padding="16" Visibility="Collapsed">
  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/>  <!-- Header with actions -->
      <RowDefinition Height="*"/>      <!-- Two-column content -->
    </Grid.RowDefinitions>
    
    <!-- Header -->
    <Grid>
      <TextBlock Text="{x:Bind SelectedModule.Name} Details" 
                 FontSize="14" FontWeight="SemiBold" Foreground="#CCCCCC"/>
      <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
        <Button Content="↻ Reconnect" Foreground="#4EC9B0" 
                Style="{StaticResource ToolbarButtonStyle}"/>
        <Button Content="⚙ Configure" Foreground="#DCDCAA" 
                Style="{StaticResource ToolbarButtonStyle}"/>
        <Button Content="✕" Style="{StaticResource ToolbarButtonStyle}"
                Click="CloseDetailPanel_Click"/>
      </StackPanel>
    </Grid>
    
    <!-- Two-column: Properties | Recent Events -->
    <Grid Grid.Row="1" Margin="0,12,0,0">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="*"/>
      </Grid.ColumnDefinitions>
      
      <!-- Properties Column -->
      <StackPanel>
        <TextBlock Text="PROPERTIES" Foreground="#858585" FontSize="11" 
                   FontWeight="SemiBold" Margin="0,0,0,8"/>
        <ItemsRepeater ItemsSource="{x:Bind SelectedModule.Properties}">
          <!-- Key-value pairs -->
        </ItemsRepeater>
      </StackPanel>
      
      <!-- Recent Events Column -->
      <StackPanel Grid.Column="1">
        <TextBlock Text="RECENT EVENTS" Foreground="#858585" FontSize="11" 
                   FontWeight="SemiBold" Margin="0,0,0,8"/>
        <ItemsRepeater ItemsSource="{x:Bind SelectedModule.RecentEvents}">
          <!-- Event timeline -->
        </ItemsRepeater>
      </StackPanel>
    </Grid>
  </Grid>
</Border>
```

---

### 6. Log Panel Content

**Filter Bar:**
```xml
<Grid Background="#2D2D2D" Padding="12,8" BorderBrush="#3C3C3C" BorderThickness="0,0,0,1">
  <StackPanel Orientation="Horizontal" Spacing="12">
    <StackPanel Orientation="Horizontal" Spacing="4">
      <TextBlock Text="Level:" Foreground="#858585" FontSize="11" VerticalAlignment="Center"/>
      <ComboBox Width="100" Background="#3C3C3C" SelectedItem="{x:Bind LogLevel, Mode=TwoWay}">
        <ComboBoxItem Content="All"/>
        <ComboBoxItem Content="Debug"/>
        <ComboBoxItem Content="Info"/>
        <ComboBoxItem Content="Warning"/>
        <ComboBoxItem Content="Error"/>
      </ComboBox>
    </StackPanel>
    <StackPanel Orientation="Horizontal" Spacing="4">
      <TextBlock Text="Module:" Foreground="#858585" FontSize="11" VerticalAlignment="Center"/>
      <ComboBox Width="140" Background="#3C3C3C" SelectedItem="{x:Bind LogModule, Mode=TwoWay}"/>
    </StackPanel>
  </StackPanel>
  
  <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
    <Button Content="🗑 Clear" Foreground="#F48771" Style="{StaticResource ToolbarButtonStyle}"/>
    <Button Content="📋 Copy All" Foreground="#4EC9B0" Style="{StaticResource ToolbarButtonStyle}"/>
  </StackPanel>
</Grid>
```

**Log Entry Template (with colored left border):**
```xml
<Border BorderBrush="{x:Bind LevelColor}" BorderThickness="2,0,0,0"
        Background="{x:Bind AlternatingBackground}" Padding="12,4">
  <Grid>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width="100"/>  <!-- Timestamp -->
      <ColumnDefinition Width="60"/>   <!-- Level badge -->
      <ColumnDefinition Width="80"/>   <!-- Module -->
      <ColumnDefinition Width="*"/>    <!-- Message -->
    </Grid.ColumnDefinitions>
    
    <TextBlock Text="{x:Bind Timestamp}" Foreground="#858585" FontSize="12"/>
    
    <Border Grid.Column="1" Background="{x:Bind LevelBackground}" 
            CornerRadius="3" Padding="6,0" HorizontalAlignment="Left">
      <TextBlock Text="{x:Bind Level}" Foreground="{x:Bind LevelColor}" 
                 FontSize="11" FontWeight="SemiBold"/>
    </Border>
    
    <TextBlock Grid.Column="2" Text="{x:Bind Module}" Foreground="#569CD6" FontSize="12"/>
    
    <TextBlock Grid.Column="3" Text="{x:Bind Message}" Foreground="#CCCCCC" FontSize="12"/>
  </Grid>
</Border>
```

---

### 7. Performance Panel Content

**Card-based layout:**
```xml
<ItemsRepeater>
  <ItemsRepeater.Layout>
    <UniformGridLayout MinItemWidth="180" MinItemHeight="120" 
                       ItemsStretch="Fill" MinRowSpacing="12" MinColumnSpacing="12"/>
  </ItemsRepeater.Layout>
</ItemsRepeater>
```

**Performance Card Template:**
```xml
<Border Background="#1E1E1E" BorderBrush="#3C3C3C" BorderThickness="1" 
        CornerRadius="6" Padding="16">
  <StackPanel>
    <TextBlock Text="MEMORY" Foreground="#858585" FontSize="11" 
               FontWeight="SemiBold" CharacterSpacing="50"/>
    <TextBlock Text="245 MB" Foreground="#4EC9B0" FontSize="24" FontWeight="SemiBold"/>
    <TextBlock Text="Working Set • Peak: 312 MB" Foreground="#858585" FontSize="11"/>
    
    <!-- Progress bar for memory -->
    <Grid Height="4" Background="#3C3C3C" CornerRadius="2" Margin="0,12,0,0">
      <Border Width="35%" Background="#4EC9B0" CornerRadius="2" HorizontalAlignment="Left"/>
    </Grid>
  </StackPanel>
</Border>
```

---

### 8. Status Bar

**Always visible at bottom of window:**
```xml
<Grid Height="22" Background="#007ACC">
  <StackPanel Orientation="Horizontal" Spacing="16" Margin="12,0" VerticalAlignment="Center">
    <TextBlock Text="⬡ OneRoom Health Kiosk v2.1.0" Foreground="White" FontSize="11"/>
    <TextBlock Text="|" Foreground="White" Opacity="0.5"/>
    <TextBlock Text="🔌 5/6 modules connected" Foreground="White" FontSize="11"/>
  </StackPanel>
  
  <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="16" 
              Margin="12,0" VerticalAlignment="Center">
    <TextBlock x:Name="LastRefreshText" Text="Last refresh: 09:48:25" 
               Foreground="White" FontSize="11"/>
    <TextBlock Text="|" Foreground="White" Opacity="0.5"/>
    <TextBlock Text="Ctrl+Shift+Q to exit" Foreground="White" FontSize="11" Opacity="0.7"/>
  </StackPanel>
</Grid>
```

---

## Implementation Status

All items below have been implemented.

### XAML Changes (MainWindow.xaml)

- [x] Add Title Bar grid at top
- [x] Restructure DebugPanel into Main Toolbar + Secondary Toolbar
- [x] Remove individual panel toggle buttons — replaced with tabbed interface
- [x] Create TabbedBottomPanel with tab bar (Health, Logs, Performance, Device Control)
- [x] Move panels into TabbedBottomPanel
- [x] Add ModuleDetailPanel for drill-down view
- [x] Add Status Bar at bottom
- [x] Apply new color scheme throughout
- [x] Update all font families to Cascadia Code/Consolas
- [x] Add Export Diagnostics button
- [x] Add Reset WebView button
- [x] Add Speaker selector alongside Camera and Microphone selectors

### Code-Behind Changes

- [x] Tab switching logic with timer management (MainWindow.Debug.cs)
- [x] Module selection state for drill-down (MainWindow.Debug.cs)
- [x] Export Diagnostics — ZIP bundle with logs, config, health snapshot (MainWindow.Debug.cs)
- [x] Reset WebView functionality (MainWindow.Debug.cs)
- [x] Panel visibility based on active tab (MainWindow.Debug.cs)
- [x] Uptime tracking for title bar and status bar (MainWindow.Debug.cs)
- [x] Connected module count for status bar (MainWindow.Debug.cs)
- [x] Reconnect button per module (MainWindow.Debug.cs)
- [x] Properties and RecentEvents in detail panel (MainWindow.Debug.cs)
- [x] Device Control tab with REST API controls (MainWindow.DeviceControl.cs)
- [x] API mode toggle with persisted preference (MainWindow.Debug.cs + UserPreferences.cs)
- [x] Camera/mic/speaker selection with persistence (MainWindow.MediaDevices.cs)

### Resource Dictionary (Styles)

Implemented in `DebugModeStyles.xaml`:
- ToolbarButtonStyle
- DebugTabButtonStyle / DebugTabButtonActiveStyle
- HealthCardStyle
- ComboBox and TextBox dark theme styles

---

## Notes for Implementation

1. **Panel State Management:** The bottom panel should always be visible when debug mode is active. Only one tab content is shown at a time.

2. **WebView Margin:** When debug mode is active, WebView margin should be `Thickness(0, 104, 0, 302)` to account for:
   - Title bar: 28px
   - Main toolbar: 40px  
   - Secondary toolbar: 36px
   - Bottom panel: ~280px
   - Status bar: 22px

3. **Data Binding:** The module health cards should bind to the existing `ModuleHealthViewModel` but expose additional properties:
   - `Properties` dictionary
   - `RecentEvents` collection
   - `StatusBackgroundBrush` (computed from status)
   - `StatusBorderBrush` (computed from status)

4. **Responsive Layout:** Use `ItemsRepeater` with `UniformGridLayout` for cards to automatically reflow based on panel width.

5. **Animation:** Consider adding subtle fade transitions when switching tabs (optional, not critical).
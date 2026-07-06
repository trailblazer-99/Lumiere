import re

with open('Controls/TransportBar.xaml', 'r', encoding='utf-8') as f:
    content = f.read()

pattern1 = r'<Grid\.Background>\s*<AcrylicBrush TintColor="Black" TintOpacity="0\.75" FallbackColor="#1A1A1A"\s*/>\s*</Grid\.Background>'
replacement1 = r'Background="{ThemeResource AcrylicBackgroundFillColorDefaultBrush}"'

content = re.sub(pattern1, replacement1, content)

pattern2 = r'CornerRadius="\{StaticResource SmallCornerRadius\}"\s*Background="#FF1C1C1C">\s*<Grid>\s*<FontIcon\s*x:Name="FallbackIcon"\s*FontSize="18"\s*Foreground="White"'
replacement2 = r'CornerRadius="{StaticResource SmallCornerRadius}"\n                        Background="{ThemeResource CardBackgroundFillColorSecondaryBrush}">\n                        <Grid>\n                            <FontIcon\n                                x:Name="FallbackIcon"\n                                FontSize="18"\n                                Foreground="{ThemeResource TextFillColorSecondaryBrush}"'

content = re.sub(pattern2, replacement2, content)

with open('Controls/TransportBar.xaml', 'w', encoding='utf-8') as f:
    f.write(content)

print("Updated TransportBar.xaml")

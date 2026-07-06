import re

with open('Pages/VideoPage.xaml', 'r', encoding='utf-8') as f:
    content = f.read()

# We want to replace from <!-- Video player container --> all the way to the end of the <Grid x:Name=\"PageContent\">
# Let's find the header end
header_pattern = r'(<!-- Video player container -->.*)    </Grid>\n</Page>'
replacement = '''        <!-- Main Library Gallery View -->
        <ScrollViewer Grid.Row="1" Visibility="{x:Bind ViewModel.OverlayVisibility, Mode=OneWay}" VerticalScrollMode="Auto" VerticalScrollBarVisibility="Auto">
            <ItemsRepeater ItemsSource="{x:Bind ViewModel.Videos, Mode=OneWay}" Margin="0,0,0,32">
                <ItemsRepeater.Layout>
                    <UniformGridLayout MinItemWidth="240" MinRowSpacing="16" MinColumnSpacing="16" />
                </ItemsRepeater.Layout>
                <ItemsRepeater.ItemTemplate>
                    <DataTemplate x:DataType="models:MediaItem">
                        <Button
                            Style="{StaticResource CardButtonStyle}"
                            Command="{Binding ElementName=PageRoot, Path=ViewModel.PlayVideoCommand}"
                            CommandParameter="{x:Bind}">
                            <controls:MediaCard
                                AccentColor="{x:Bind AccentColor}"
                                Artwork="{Binding Artwork}"
                                Subtitle="{x:Bind Artist}"
                                Title="{x:Bind Title}" />
                        </Button>
                    </DataTemplate>
                </ItemsRepeater.ItemTemplate>
            </ItemsRepeater>
        </ScrollViewer>

        <!-- Player & Sidebar Layout -->
        <Grid Grid.Row="1" ColumnSpacing="16" Visibility="{x:Bind ViewModel.PlayerVisibility, Mode=OneWay}">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="340" />
            </Grid.ColumnDefinitions>

            <!-- Video player container -->
            <Border
                x:Name="VideoPlayerContainer"
                Grid.Column="0"
                Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
                BorderThickness="1"
                CornerRadius="{StaticResource LargeCornerRadius}"
                DoubleTapped="OnVideoDoubleTapped"
                Tapped="OnVideoTapped"
                PointerWheelChanged="OnVideoPointerWheelChanged">
                <Grid>
                    <Viewbox Stretch="Uniform">
                        <MediaPlayerElement
                            x:Name="VideoPlayer"
                            AreTransportControlsEnabled="False"
                            AutoPlay="False"
                            Stretch="Fill" />
                    </Viewbox>

                    <!-- Metadata Overlay -->
                    <Border
                        x:Name="MetadataOverlay"
                        HorizontalAlignment="Right"
                        VerticalAlignment="Top"
                        Margin="24"
                        Padding="20"
                        CornerRadius="8"
                        Visibility="Collapsed"
                        Background="{ThemeResource LayerFillColorAltBrush}">
                        <Border.BackgroundTransition>
                            <BrushTransition />
                        </Border.BackgroundTransition>
                        <StackPanel Spacing="12">
                            <TextBlock Text="File Information" FontSize="20" FontWeight="SemiBold" Margin="0,0,0,4" Foreground="{ThemeResource TextFillColorPrimaryBrush}" />
                            
                            <Grid ColumnSpacing="24" RowSpacing="8">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="*" />
                                </Grid.ColumnDefinitions>
                                <Grid.RowDefinitions>
                                    <RowDefinition />
                                    <RowDefinition />
                                    <RowDefinition />
                                    <RowDefinition />
                                </Grid.RowDefinitions>

                                <TextBlock Grid.Row="0" Grid.Column="0" Text="Resolution" Foreground="{ThemeResource TextFillColorSecondaryBrush}" FontSize="12" />
                                <TextBlock Grid.Row="0" Grid.Column="1" Text="{Binding ViewModel.PlaybackService.CurrentTrack.Resolution, TargetNullValue='Unknown'}" FontSize="12" FontWeight="SemiBold" Foreground="{ThemeResource TextFillColorPrimaryBrush}" />

                                <TextBlock Grid.Row="1" Grid.Column="0" Text="Codec" Foreground="{ThemeResource TextFillColorSecondaryBrush}" FontSize="12" />
                                <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding ViewModel.PlaybackService.CurrentTrack.Codec, TargetNullValue='Unknown'}" FontSize="12" FontWeight="SemiBold" Foreground="{ThemeResource TextFillColorPrimaryBrush}" />

                                <TextBlock Grid.Row="2" Grid.Column="0" Text="Bitrate" Foreground="{ThemeResource TextFillColorSecondaryBrush}" FontSize="12" />
                                <TextBlock Grid.Row="2" Grid.Column="1" Text="{Binding ViewModel.PlaybackService.CurrentTrack.Bitrate}" FontSize="12" FontWeight="SemiBold" Foreground="{ThemeResource TextFillColorPrimaryBrush}" />

                                <TextBlock Grid.Row="3" Grid.Column="0" Text="File Path" Foreground="{ThemeResource TextFillColorSecondaryBrush}" FontSize="12" />
                                <TextBlock Grid.Row="3" Grid.Column="1" Text="{Binding ViewModel.PlaybackService.CurrentTrack.SourcePath, TargetNullValue='Unknown'}" FontSize="12" FontWeight="SemiBold" MaxWidth="250" TextWrapping="Wrap" Foreground="{ThemeResource TextFillColorPrimaryBrush}" />
                            </Grid>
                        </StackPanel>
                    </Border>
                </Grid>
            </Border>

            <!-- Video list sidebar -->
            <Border
                x:Name="VideoListBorder"
                Grid.Column="1"
                CornerRadius="{StaticResource CardCornerRadiusFull}"
                Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
                BorderThickness="1"
                Padding="4">
                <Grid RowSpacing="8">
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto"/>
                        <RowDefinition Height="*"/>
                    </Grid.RowDefinitions>
                    
                    <TextBlock Grid.Row="0" Text="Up Next" Style="{StaticResource SectionHeaderTextStyle}" Margin="12,12,12,4"/>
                    
                    <ListView
                        Grid.Row="1"
                        IsItemClickEnabled="True"
                        ItemsSource="{x:Bind ViewModel.Videos, Mode=OneWay}"
                        SelectionMode="None">
                        <ListView.ItemTemplate>
                            <DataTemplate x:DataType="models:MediaItem">
                                <Grid Padding="12,10" ColumnSpacing="12">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="Auto" />
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="Auto" />
                                    </Grid.ColumnDefinitions>

                                    <!-- Video icon in accent-tinted circle -->
                                    <Border
                                        Grid.Column="0"
                                        Width="36"
                                        Height="36"
                                        CornerRadius="18"
                                        Background="{ThemeResource CardBackgroundFillColorSecondaryBrush}"
                                        VerticalAlignment="Center">
                                        <FontIcon
                                            FontSize="14"
                                            Glyph="&#xE714;"
                                            HorizontalAlignment="Center"
                                            VerticalAlignment="Center" />
                                    </Border>

                                    <StackPanel Grid.Column="1" Spacing="2" VerticalAlignment="Center">
                                        <TextBlock
                                            FontSize="13"
                                            FontWeight="SemiBold"
                                            Text="{x:Bind Title}"
                                            MaxLines="1"
                                            TextTrimming="CharacterEllipsis" />
                                        <TextBlock
                                            Style="{StaticResource SubtleCaptionTextStyle}"
                                            Text="{x:Bind Artist}"
                                            MaxLines="1"
                                            TextTrimming="CharacterEllipsis" />
                                    </StackPanel>

                                    <Button
                                        Grid.Column="2"
                                        AutomationProperties.Name="Play video"
                                        Command="{Binding ElementName=PageRoot, Path=ViewModel.PlayVideoCommand}"
                                        CommandParameter="{x:Bind}"
                                        Style="{StaticResource TransportIconButtonStyle}"
                                        ToolTipService.ToolTip="Play">
                                        <FontIcon FontSize="14" Glyph="&#xE768;" />
                                    </Button>
                                </Grid>
                            </DataTemplate>
                        </ListView.ItemTemplate>
                    </ListView>
                </Grid>
            </Border>
        </Grid>'''

new_content = re.sub(header_pattern, replacement, content, flags=re.DOTALL)
if new_content != content:
    with open('Pages/VideoPage.xaml', 'w', encoding='utf-8') as f:
        # Don't forget to restore the end tags we matched
        f.write(new_content + "\n    </Grid>\n</Page>")
    print('Refactored VideoPage layout')
else:
    print('Failed to match pattern')

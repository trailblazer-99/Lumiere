import re

files = ['Pages/StreamingMoviesPage.xaml', 'Pages/StreamingTvShowsPage.xaml']

for filename in files:
    with open(filename, 'r', encoding='utf-8') as f:
        content = f.read()
    
    content = content.replace('SelectedValue="{x:Bind ViewModel.SelectedSortOrder, Mode=TwoWay}"', 'SelectedValue="{Binding ViewModel.SelectedSortOrder, Mode=TwoWay}"')
    
    with open(filename, 'w', encoding='utf-8') as f:
        f.write(content)

print("XAML bindings fixed")

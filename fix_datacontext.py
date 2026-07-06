import re

files = ['Pages/StreamingMoviesPage.xaml.cs', 'Pages/StreamingTvShowsPage.xaml.cs', 'Pages/StreamingMusicPage.xaml.cs']

for filename in files:
    with open(filename, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # Add this.DataContext = this; after InitializeComponent()
    content = content.replace('this.InitializeComponent();', 'this.InitializeComponent();\n            this.DataContext = this;')
    
    with open(filename, 'w', encoding='utf-8') as f:
        f.write(content)

print("DataContext fixed")

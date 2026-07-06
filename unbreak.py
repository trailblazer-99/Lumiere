import re

with open('Pages/SettingsPage.xaml', 'r', encoding='utf-8') as f:
    content = f.read()

# I want to find all tags that end with \" />.
# If the tag name is NOT in the self-closing list, I will change \" /> back to \">.
self_closing_tags = {'TextBlock', 'ToggleSwitch', 'Slider', 'FontIcon', 'ColumnDefinition', 'RowDefinition', 'ComboBoxItem', 'HyperlinkButton', 'Setter'}

def repl(m):
    tag_name = m.group(1)
    attrs = m.group(2)
    # m.group(0) is the entire tag e.g. <Border Style="..." />
    
    # If the tag is explicitly known to be self-closing, leave it.
    if tag_name in self_closing_tags:
        return f'<{tag_name}{attrs}/>'
        
    # Wait, earlier I did a patch that added IsOn, SelectedIndex, Value.
    # Those were added to ToggleSwitch, ComboBox, Slider.
    # ComboBoxes were NOT self-closing, they had <ComboBoxItem> inside them!
    # Let's restore \" /> to \"> for everything NOT in self_closing_tags.
    
    return f'<{tag_name}{attrs}>'

content = re.sub(r'<([a-zA-Z0-9:]+)([^>]*?)\s*/>', repl, content)

with open('Pages/SettingsPage.xaml', 'w', encoding='utf-8') as f:
    f.write(content)
print('Unbroken')

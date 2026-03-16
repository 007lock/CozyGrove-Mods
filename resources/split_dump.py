import os
import re

def split_dump_file():
    input_file = "dump.cs"
    output_dir = "dump_split"
    
    if not os.path.exists(output_dir):
        os.makedirs(output_dir)
        
    with open(input_file, "r", encoding="utf-8") as f:
        current_namespace = "Global"
        current_file = None
        
        namespace_pattern = re.compile(r"^// Namespace:\s*(.*)$")
        
        file_handles = {}
        
        def get_file_handle(ns):
            # Clean namespace for filename
            ns_clean = ns.strip()
            if not ns_clean:
                ns_clean = "Global"
            
            # Replace invalid path characters
            ns_clean = re.sub(r'[<>:"/\\|?*]', '_', ns_clean)
            
            filename = os.path.join(output_dir, f"{ns_clean}.cs")
            if ns_clean not in file_handles:
                file_handles[ns_clean] = open(filename, "w", encoding="utf-8")
            return file_handles[ns_clean]

        current_handle = get_file_handle("Global")
        
        for line in f:
            match = namespace_pattern.match(line)
            if match:
                ns = match.group(1)
                current_handle = get_file_handle(ns)
            
            current_handle.write(line)
            
        for handle in file_handles.values():
            handle.close()

if __name__ == "__main__":
    split_dump_file()
    print("Successfully split dump.cs by namespace into dump_split/ directory.")

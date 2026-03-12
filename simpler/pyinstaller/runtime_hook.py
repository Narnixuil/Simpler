import os

# Prevent external Python env vars from breaking the embedded interpreter
os.environ.pop("PYTHONHOME", None)
os.environ.pop("PYTHONPATH", None)

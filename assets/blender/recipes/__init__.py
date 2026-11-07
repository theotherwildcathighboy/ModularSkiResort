"""
recipes/ - one module per building recipe (L2..L5, school, patrol P1/P2).
bldg_recipes.load_recipes() merges them in sorted file order. Each module defines RECIPE (a
bldg_recipes.Recipe) or RECIPES (a sequence). Keys and aliases must be unique. Modules must be
import-safe (no bpy work at import). See bldg_recipes.py for the schema and the L1 recipe as the
worked example. lodge.py is the engine.
"""

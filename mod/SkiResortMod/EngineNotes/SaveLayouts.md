# SaveLayouts: how to bump a persisted layout without breaking saves

`SaveLayouts` is the frozen, append-only table of every persisted type. tests/SaveBatch checks the entries against the serializers.

To bump a layout:

1. Append the new fields at the END of `Serialize`.
2. Bump `CurrentVersion`.
3. Read the new fields under `if (m_Version >= N)` in `Deserialize`.
4. Append ONE entry to the type's row in `SaveLayouts`.
5. Write the new changelog line with "Do not load in older versions: yes".

A new persisted concept is a new type with a new row. Never edit a shipped entry. Never reorder the fields of a row. Never delete the
row of a shipped type. The bytes are in players' saves.

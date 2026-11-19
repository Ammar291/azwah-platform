# Members module

**Owns:** `FamilyMember` and its value lists (`Domain/Members`). Members live inside the `Household` aggregate and are
changed only through it

**Status:** Changed through the Households endpoints (`POST` and `PATCH …/members`). Any change clears the roster
confirmation

**Other modules may use:** Member reads through Households' access rules; `FamilyMember.AgeOn`

**Must not:** Decide which instrument or module a member answers (Assignments does); store names or identity numbers

# Households module

**Owns:** The `Household` aggregate (`Domain/Households`) with its roster and its memberships
(`HouseholdMembership`); `IHouseholdRepository`; the household use cases

**Status:** Participant endpoints (D-47, D-50): create (the caller becomes manager), read, withdraw, list, read, add and
merge-patch members, and confirm the roster, which runs Assignments' `IHouseholdAssignmentService` for the current wave
in the same transaction. Removing a member locks the household, as assignment runs do, and closes what they left open
in the same transaction (Sessions' `IMemberAssessmentCloser`). Every change is audited. Joining an existing household is
not built

**Other modules may use:** `IHouseholdRepository.GetAsync` after an access check; `StatusOfMemberHouseholdAsync`

**Must not:** Return a household without the caller's manager check (`ManagedHouseholds.LoadAsync`); delete members
(deactivate them); deactivate the manager's own member; accept eligibility decisions from a client

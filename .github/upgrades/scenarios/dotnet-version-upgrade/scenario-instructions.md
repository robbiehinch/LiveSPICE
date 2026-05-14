## Scenario Parameters
- **Target Framework**: net10.0 (.NET 10.0 LTS)
- **Source Branch**: master
- **Working Branch**: upgrade-to-NET10
- **Solution**: LiveSPICE.sln

## Strategy
**Selected**: Bottom-Up (Dependency-First)
**Rationale**: 12 projects, 5-tier dependency graph, significant breaking changes in upper tiers

### Execution Constraints
- Strict tier ordering: Tier N must complete and validate before Tier N+1
- Between-tier validation: confirm higher tiers still build after each tier completes
- Each tier: update TFM + packages → build → fix errors → run tests
- Libraries move netstandard2.0 → net10.0; apps move net6.0/net8.0-windows → net10.0-windows

## Preferences
- **Flow Mode**: Automatic
- **Commit Strategy**: After Each Task
- **Pace**: Standard

## Decisions
- User confirmed .NET 10.0 as target framework, Automatic flow mode
- Bottom-Up strategy auto-selected based on deep dependency graph + high issue count

## Custom Instructions
*(none yet)*
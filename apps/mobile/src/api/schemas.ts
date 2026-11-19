import { z } from 'zod';

/** GET /health/live and /health/ready (backend/FamilyResilience.Api/Health/HealthEndpoints.cs). */
export const healthResponseSchema = z.object({
  status: z.enum(['Healthy', 'Degraded', 'Unhealthy']),
});

export type HealthResponse = z.infer<typeof healthResponseSchema>;

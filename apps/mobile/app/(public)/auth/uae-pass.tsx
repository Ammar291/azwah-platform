import { SimulatedUaePass } from '@/features/auth/SimulatedUaePass';

/** Development fixtures only: the simulated UAE PASS (labelled). Other builds are sent back to the welcome screen. */
export default function UaePassSimulated() {
  return <SimulatedUaePass />;
}

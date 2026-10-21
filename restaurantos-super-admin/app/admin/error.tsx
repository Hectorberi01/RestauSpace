'use client';import {ErrorState} from '../../src/components/ui/shared';export default function Error({reset}:{reset:()=>void}){return <ErrorState retry={reset}/>}

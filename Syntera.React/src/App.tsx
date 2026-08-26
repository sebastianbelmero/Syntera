import './App.css'
import { useWeather } from './hooks/useWeather'

function App() {
  const { data: forecasts, isLoading, isError, error } = useWeather();
  if (isLoading) return <p>Loading Weather...</p>
  if (isError) return <p>Error: {error.message}</p>
  return (
    <>
      <section id="center">
        <h2>Weather Forecast</h2>
        <ul>
          {/* Gunakan optional chaining (?.) untuk keamanan ekstra */}
          {forecasts?.map((forecast, index) => (
            <li key={index}>
              {forecast.date}: {forecast.summary} ({forecast.temperatureC}°C)
            </li>
          ))}
        </ul>
      </section>
    </>
  )
}

export default App
